// MLOmega V19 — E25
// UIRuntime: the renderer that turns the broker's arbitrated ActiveIntents into
// live liquid-glass components and back. It:
//   * subscribes to the UIIntentBroker (IntentAdmitted / IntentFading / IntentDropped);
//   * maps each intent's `component` field to a concrete UIComponentBase type
//     using the §13.1 design-system table (UIComponentRegistry);
//   * pools components per type (simple free-list) so admit/drop churn does not
//     allocate GameObjects every frame;
//   * wires each component with the SceneCache (anchoring) and the IReceiptSink
//     (receipts) via a shared UIComponentContext;
//   * shares one LiquidGlass Material across every panel so they batch and the
//     Kawase blur is sampled once.
// The broker owns arbitration/priority/TTL/density; the runtime owns instantiation
// and lifecycle only — it never second-guesses the broker's decisions.
using System;
using System.Collections;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using MLOmega.XR.Core;
using MLOmega.XR.Scene;
using MLOmega.XR.UI.Components;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;

namespace MLOmega.XR.UI
{
    public sealed class UIRuntime : MonoBehaviour
    {
        [SerializeField] private UIIntentBroker _broker;
        [SerializeField] private SceneCache _sceneCache;
        [SerializeField] private UITheme _theme;
        [SerializeField] private Camera _camera;
        [SerializeField] private SessionPairing _pairing;

        [Tooltip("LiquidGlass material shared by all panels. If null, built from the shader at runtime.")]
        [SerializeField] private Material _glassMaterial;

        [Tooltip("Component that receives outbound receipts (transport sink).")]
        [SerializeField] private MonoBehaviour _receiptSinkBehaviour; // must implement IReceiptSink

        private IReceiptSink _sink;
        private UIComponentContext _context;

        // ui_intent_id -> live component instance rendering it.
        private readonly Dictionary<string, UIComponentBase> _live =
            new Dictionary<string, UIComponentBase>();
        // component-key -> free-list of recycled instances.
        private readonly Dictionary<string, Stack<UIComponentBase>> _pool =
            new Dictionary<string, Stack<UIComponentBase>>();

        private Transform _root;
        private Coroutine _replayFlow;
        private int _replayGeneration;
        private VideoPlayer _replayPlayer;
        private RenderTexture _replayRender;
        private Texture2D _replayTexture;

        private void Awake()
        {
            if (_broker == null) _broker = FindAnyObjectByType<UIIntentBroker>();
            if (_sceneCache == null) _sceneCache = FindAnyObjectByType<SceneCache>();
            if (_camera == null) _camera = Camera.main;
            if (_pairing == null) _pairing = FindAnyObjectByType<SessionPairing>();
            _sink = _receiptSinkBehaviour as IReceiptSink;

            if (_glassMaterial == null)
            {
                Shader s = Shader.Find("MLOmega/LiquidGlass");
                if (s != null) _glassMaterial = new Material(s);
            }

            _context = new UIComponentContext(_sceneCache, _glassMaterial, _camera);

            var rootGo = new GameObject("UIRuntimeRoot");
            rootGo.transform.SetParent(transform, false);
            _root = rootGo.transform;
        }

        private void OnEnable()
        {
            if (_broker == null) return;
            _broker.IntentAdmitted += OnAdmitted;
            _broker.IntentFading += OnFading;
            _broker.IntentDropped += OnDropped;
        }

        private void OnDisable()
        {
            if (_broker == null) return;
            _broker.IntentAdmitted -= OnAdmitted;
            _broker.IntentFading -= OnFading;
            _broker.IntentDropped -= OnDropped;
            StopReplayMedia();
        }

        // ------------------------------------------------------------------
        //  Broker event handlers
        // ------------------------------------------------------------------

        private void OnAdmitted(ActiveIntent active)
        {
            UIIntent intent = active.Intent;
            string id = intent.UiIntentId;

            // Dedup refresh: same id already live -> just update its payload.
            if (_live.TryGetValue(id, out UIComponentBase existing))
            {
                existing.Refresh(intent);
                BeginReplayMedia(existing, intent);
                return;
            }

            string key = UIComponentRegistry.KeyFor(intent.Component);
            if (key == null)
            {
                Debug.LogWarning($"[UIRuntime] no component mapping for '{intent.Component}' (intent {id}).");
                return;
            }

            UIComponentBase comp = Rent(key);
            if (comp == null) return;
            _live[id] = comp;
            comp.Admit(intent, _sink, OnComponentRecycled);
            BeginReplayMedia(comp, intent);
        }

        private void OnFading(ActiveIntent active, UIIntentDropReason reason)
        {
            if (_live.TryGetValue(active.Intent.UiIntentId, out UIComponentBase comp))
            {
                // A user suppression is the only "dismissed" receipt (§13.3);
                // everything else fades silently (drop-reason already journaled).
                bool userDismissed = reason == UIIntentDropReason.UserSuppressed;
                comp.BeginFadeOut(userDismissed);
            }
        }

        private void OnDropped(UIIntent intent, UIIntentDropReason reason)
        {
            // The component fades itself out on IntentFading and returns to the pool
            // via OnComponentRecycled; if it was never shown (e.g. no mapping) there
            // is nothing to do. We keep the _live map cleaned on recycle.
        }

        // ------------------------------------------------------------------
        //  Pooling
        // ------------------------------------------------------------------

        private UIComponentBase Rent(string key)
        {
            if (_pool.TryGetValue(key, out Stack<UIComponentBase> stack) && stack.Count > 0)
            {
                return stack.Pop();
            }
            return Create(key);
        }

        private UIComponentBase Create(string key)
        {
            System.Type type = UIComponentRegistry.TypeFor(key);
            if (type == null) return null;

            var go = new GameObject(key);
            go.transform.SetParent(_root, false);
            var comp = (UIComponentBase)go.AddComponent(type);
            comp.Configure(_context, _theme);
            go.SetActive(false);
            return comp;
        }

        private void OnComponentRecycled(UIComponentBase comp)
        {
            if (comp is VirtualScreen) StopReplayMedia();
            // Remove from the live map (find by value — the set is tiny, bounded by
            // the density cap, so a linear scan is fine).
            string foundId = null;
            foreach (KeyValuePair<string, UIComponentBase> kv in _live)
            {
                if (ReferenceEquals(kv.Value, comp)) { foundId = kv.Key; break; }
            }
            if (foundId != null) _live.Remove(foundId);

            string key = comp.ComponentKey;
            if (!_pool.TryGetValue(key, out Stack<UIComponentBase> stack))
            {
                stack = new Stack<UIComponentBase>();
                _pool[key] = stack;
            }
            stack.Push(comp);
        }

        // ------------------------------------------------------------------
        // Replay media: authenticated HTTP refs -> actual VirtualScreen texture.
        // ------------------------------------------------------------------

        private sealed class ReplayRef
        {
            public string Url;
            public string At;
            public bool IsVideo;
        }

        private void BeginReplayMedia(UIComponentBase comp, UIIntent intent)
        {
            if (!(comp is VirtualScreen screen) ||
                !string.Equals(IntentRead.Content(intent, "kind"), "replay", StringComparison.OrdinalIgnoreCase))
                return;
            List<ReplayRef> refs = ReadReplayRefs(intent);
            StopReplayMedia();
            if (refs.Count > 0) _replayFlow = StartCoroutine(PlayReplay(screen, refs, ++_replayGeneration));
        }

        private static List<ReplayRef> ReadReplayRefs(UIIntent intent)
        {
            var refs = new List<ReplayRef>();
            AddRefs(intent?.Content, "frames", false, refs);
            AddRefs(intent?.Content, "clips", true, refs);
            refs.Sort((a, b) => string.CompareOrdinal(a.At ?? "", b.At ?? ""));
            return refs;
        }

        private static void AddRefs(Dictionary<string, object> content, string key, bool video,
            List<ReplayRef> output)
        {
            if (content == null || !content.TryGetValue(key, out object raw) || raw == null) return;
            JToken token;
            try { token = raw as JToken ?? JToken.FromObject(raw); }
            catch { return; }
            if (!(token is JArray array)) return;
            foreach (JToken item in array)
            {
                string url = item.Value<string>("ref");
                if (string.IsNullOrWhiteSpace(url)) continue;
                output.Add(new ReplayRef { Url = url, At = item.Value<string>("at"), IsVideo = video });
            }
        }

        private string AuthenticatedMediaUrl(string mediaRef)
        {
            if (_pairing == null || string.IsNullOrWhiteSpace(_pairing.ActiveBaseUrl) ||
                string.IsNullOrWhiteSpace(_pairing.SessionId) || string.IsNullOrWhiteSpace(_pairing.Token)) return null;
            string url = mediaRef.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? mediaRef
                : _pairing.ActiveBaseUrl.TrimEnd('/') + "/" + mediaRef.TrimStart('/');
            string separator = url.Contains("?") ? "&" : "?";
            return url + separator + "session_id=" + UnityWebRequest.EscapeURL(_pairing.SessionId) +
                   "&token=" + UnityWebRequest.EscapeURL(_pairing.Token);
        }

        private IEnumerator PlayReplay(VirtualScreen screen, List<ReplayRef> refs, int generation)
        {
            foreach (ReplayRef media in refs)
            {
                if (generation != _replayGeneration || screen == null) yield break;
                string url = AuthenticatedMediaUrl(media.Url);
                if (string.IsNullOrWhiteSpace(url)) yield break;
                if (media.IsVideo)
                {
                    _replayPlayer = screen.gameObject.AddComponent<VideoPlayer>();
                    _replayRender = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32);
                    _replayRender.Create();
                    _replayPlayer.playOnAwake = false;
                    _replayPlayer.source = VideoSource.Url;
                    _replayPlayer.url = url;
                    _replayPlayer.renderMode = VideoRenderMode.RenderTexture;
                    _replayPlayer.targetTexture = _replayRender;
                    _replayPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
                    _replayPlayer.Prepare();
                    float deadline = Time.realtimeSinceStartup + 15f;
                    while (!_replayPlayer.isPrepared && Time.realtimeSinceStartup < deadline) yield return null;
                    if (_replayPlayer.isPrepared)
                    {
                        screen.SetSurfaceTexture(_replayRender);
                        _replayPlayer.Play();
                        yield return null;
                        deadline = Time.realtimeSinceStartup + 180f;
                        while (_replayPlayer != null && _replayPlayer.isPlaying &&
                               Time.realtimeSinceStartup < deadline) yield return null;
                    }
                    CleanupVideo();
                }
                else
                {
                    using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url, true))
                    {
                        yield return request.SendWebRequest();
                        if (generation != _replayGeneration) yield break;
                        if (request.result == UnityWebRequest.Result.Success)
                        {
                            if (_replayTexture != null) Destroy(_replayTexture);
                            _replayTexture = DownloadHandlerTexture.GetContent(request);
                            screen.SetSurfaceTexture(_replayTexture);
                            yield return new WaitForSecondsRealtime(1.5f);
                        }
                    }
                }
            }
            _replayFlow = null;
        }

        private void StopReplayMedia()
        {
            _replayGeneration++;
            if (_replayFlow != null) StopCoroutine(_replayFlow);
            _replayFlow = null;
            CleanupVideo();
            if (_replayTexture != null) Destroy(_replayTexture);
            _replayTexture = null;
        }

        private void CleanupVideo()
        {
            if (_replayPlayer != null)
            {
                _replayPlayer.Stop();
                Destroy(_replayPlayer);
            }
            _replayPlayer = null;
            if (_replayRender != null)
            {
                _replayRender.Release();
                Destroy(_replayRender);
            }
            _replayRender = null;
        }
    }
}
