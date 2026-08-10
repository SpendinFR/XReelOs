// MLOmega V19 — E60
// Consumes bounded tts_audio DataChannel messages and plays PCM16 WAV locally.
using System;
using System.Text;
using MLOmega.XR.Transport;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MLOmega.XR.UI
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class TtsAudioPlayer : MonoBehaviour
    {
        private const int MaxBase64Chars = 240000;
        [SerializeField] private LiveTransportBridge _transport;
        [SerializeField] private AudioSource _source;

        public int PlayedCount { get; private set; }
        public string LastError { get; private set; }

        private void Awake()
        {
            if (_transport == null) _transport = FindAnyObjectByType<LiveTransportBridge>();
            if (_source == null) _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
        }

        private void OnEnable()
        {
            if (_transport != null) _transport.MessageReceived += OnTransportMessage;
        }

        private void OnDisable()
        {
            if (_transport != null) _transport.MessageReceived -= OnTransportMessage;
        }

        private void OnTransportMessage(string json)
        {
            if (string.IsNullOrEmpty(json) || json.IndexOf("\"tts_audio\"", StringComparison.Ordinal) < 0)
                return;
            try
            {
                JObject obj = JObject.Parse(json);
                if (!string.Equals((string)obj["type"], "tts_audio", StringComparison.Ordinal) ||
                    !string.Equals((string)obj["format"], "wav", StringComparison.OrdinalIgnoreCase))
                    return;
                string b64 = (string)obj["audio_b64"];
                if (string.IsNullOrEmpty(b64) || b64.Length > MaxBase64Chars)
                    throw new FormatException("tts_audio payload is empty or over budget");
                byte[] wav = Convert.FromBase64String(b64);
                if (!TryDecodePcm16Wav(wav, out float[] samples, out int channels, out int rate))
                    throw new FormatException("unsupported WAV (expected PCM16 RIFF)");
                int frames = samples.Length / channels;
                AudioClip clip = AudioClip.Create("VikiTts", frames, channels, rate, false);
                clip.SetData(samples, 0);
                _source.PlayOneShot(clip);
                Destroy(clip, clip.length + 0.5f);
                PlayedCount++;
                LastError = null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Debug.LogWarning("[TtsAudioPlayer] " + ex.Message);
            }
        }

        public static bool TryDecodePcm16Wav(byte[] wav, out float[] samples,
            out int channels, out int sampleRate)
        {
            samples = null; channels = 0; sampleRate = 0;
            if (wav == null || wav.Length < 44 ||
                Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" ||
                Encoding.ASCII.GetString(wav, 8, 4) != "WAVE") return false;

            int bits = 0, format = 0, dataOffset = -1, dataSize = 0;
            int offset = 12;
            while (offset + 8 <= wav.Length)
            {
                string id = Encoding.ASCII.GetString(wav, offset, 4);
                int size = BitConverter.ToInt32(wav, offset + 4);
                if (size < 0 || offset + 8 + size > wav.Length) return false;
                if (id == "fmt " && size >= 16)
                {
                    format = BitConverter.ToInt16(wav, offset + 8);
                    channels = BitConverter.ToInt16(wav, offset + 10);
                    sampleRate = BitConverter.ToInt32(wav, offset + 12);
                    bits = BitConverter.ToInt16(wav, offset + 22);
                }
                else if (id == "data")
                {
                    dataOffset = offset + 8;
                    dataSize = size;
                }
                offset += 8 + size + (size & 1);
            }
            if (format != 1 || bits != 16 || channels < 1 || channels > 2 ||
                sampleRate < 8000 || dataOffset < 0 || dataSize < 2) return false;
            int count = dataSize / 2;
            samples = new float[count];
            for (int i = 0; i < count; i++)
                samples[i] = BitConverter.ToInt16(wav, dataOffset + i * 2) / 32768f;
            return true;
        }
    }
}
