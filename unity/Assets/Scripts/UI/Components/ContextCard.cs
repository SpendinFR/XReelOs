// MLOmega V19 — E25
// ContextCard (§13.1): a short 2-3 line side panel carrying a memory / rule /
// relation from BrainLive, with its source. Anchored as a lateral head-locked
// panel. Being a BrainLive contextual hint it is, by the truth ladder, usually
// "probable"/"inferred": it therefore shows the discreet truth chip and, for
// relational readings, the hypothesis label — never presented as observation
// (§17.2, §17.3 social rule). Emits displayed/seen/dismissed via the base.
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    public sealed class ContextCard : UIComponentBase, IManipulablePanel, IManipulationFeedback
    {
        [SerializeField] private Vector2 _size = new Vector2(0.42f, 0.20f);
        [SerializeField] private Vector3 _lateralOffset = new Vector3(0.34f, 0.02f, 1.1f);

        [Header("E59 — hand manipulation (opt-in)")]
        [Tooltip("When true, the user can grab/resize/close this card by hand; once moved " +
                 "it stops head-locking and stays where placed. Default false so automatic " +
                 "contextual hints keep their lateral head-locked placement.")]
        [SerializeField] private bool _userManipulable;
        [SerializeField] private Vector2 _minSize = new Vector2(0.22f, 0.12f);
        [SerializeField] private Vector2 _maxSize = new Vector2(1.2f, 0.9f);

        private GlassPanel _panel;
        private bool _registered;
        private bool _userPlaced;  // set once the user grabs it → stop head-locking
        private bool _minimised;
        private Vector3 _restorePosition;
        private Quaternion _restoreRotation;
        private Vector2 _restoreSize;

        public override string ComponentKey => "context_card";

        protected override void OnConfigured()
        {
            _panel = new GlassPanel(transform, _size, Theme,
                Context != null ? Context.GlassMaterial : null,
                withTitle: true, withBody: true, withTruthChip: true);
        }

        protected override void Bind(Contracts.V19.UIIntent intent)
        {
            string title = IntentRead.Content(intent, "title", "Context");
            string body = IntentRead.Content(intent, "text", IntentRead.Content(intent, "body", ""));
            string source = IntentRead.Content(intent, "source", null);

            if (_panel.Title != null) _panel.Title.text = title;
            if (_panel.Body != null)
            {
                _panel.Body.text = string.IsNullOrEmpty(source)
                    ? body
                    : $"{body}\n<size=80%><color=#9FB3C8>src: {source}</color></size>";
            }
            PlaceLateral();
        }

        protected override void OnTruth(TruthDescriptor truth)
        {
            if (_panel == null) return;
            _panel.SetAccent(truth.Accent);
            if (_panel.TruthChip != null)
            {
                _panel.TruthChip.text = TruthChipText(truth);
            }
        }

        public static string TruthChipText(TruthDescriptor truth)
        {
            if (truth.ShowHypothesisLabel) return "hypothesis";
            if (truth.ShowProbableBadge) return "probable";
            if (!string.IsNullOrEmpty(truth.AgeText)) return truth.AgeText;
            return string.Empty;
        }

        private void PlaceLateral()
        {
            Camera cam = Context != null ? Context.Camera : Camera.main;
            if (cam == null) return;
            transform.SetPositionAndRotation(
                cam.transform.TransformPoint(_lateralOffset),
                Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up));
        }

        protected override void Update()
        {
            base.Update();
            if (Phase != UIComponentPhase.Idle)
            {
                // E59: once the user has grabbed a manipulable card, stop head-locking
                // so it stays where they put it; otherwise keep it lateral head-locked.
                if (!(_userManipulable && _userPlaced)) PlaceLateral();
                _panel?.SetAlpha(CurrentAlpha);
                if (_userManipulable && !_registered && !_minimised)
                {
                    ManipulablePanelRegistry.Register(this);
                    _registered = true;
                }
            }
            else
            {
                _userPlaced = false;
                _minimised = false;
                if (_registered) { ManipulablePanelRegistry.Unregister(this); _registered = false; }
            }
        }

        private void OnDisable()
        {
            if (_registered) { ManipulablePanelRegistry.Unregister(this); _registered = false; }
        }

        // ------------------------------------------------------------------
        //  E59 — IManipulablePanel (opt-in via _userManipulable)
        // ------------------------------------------------------------------

        public string PersistenceKey => ComponentKey;
        public Transform PanelTransform => transform;
        public Vector2 PanelSize => _size;
        public bool IsManipulable => _userManipulable && Phase != UIComponentPhase.Idle;
        public bool LockAspectRatio => false; // a text card resizes freely
        public Vector2 MinSize => _minSize;
        public Vector2 MaxSize => _maxSize;
        public bool IsMinimised => _minimised;

        public void MoveTo(Vector3 worldPosition) { _userPlaced = true; transform.position = worldPosition; }

        public void ResizeTo(Vector2 size)
        {
            _userPlaced = true;
            _size = size;
            if (_panel != null) _panel.Root.sizeDelta = size;
        }

        public void CloseFromGesture() => RaiseDismissed();

        public void MinimiseFromGesture()
        {
            if (_minimised) return;
            _minimised = true;
            _restorePosition = transform.position;
            _restoreRotation = transform.rotation;
            _restoreSize = _size;
            _userPlaced = true;
            ResizeTo(new Vector2(0.1f, 0.1f));
            Camera cam = Context != null ? Context.Camera : Camera.main;
            if (cam != null)
            {
                transform.SetPositionAndRotation(
                    cam.transform.TransformPoint(new Vector3(0.55f, -0.28f, 1.1f)),
                    Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up));
            }
        }

        public void RestoreFromGesture()
        {
            if (!_minimised) return;
            _minimised = false;
            transform.SetPositionAndRotation(_restorePosition, _restoreRotation);
            ResizeTo(_restoreSize);
        }

        public void SetManipulationFeedback(bool active, bool resizing) =>
            _panel?.SetManipulationFeedback(active, resizing);
    }
}
