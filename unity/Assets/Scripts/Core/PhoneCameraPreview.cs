using UnityEngine;

namespace MLOmega.XR.Core
{
    /// <summary>Renders the real rear camera behind the flat PhoneOnly UI.</summary>
    public sealed class PhoneCameraPreview : MonoBehaviour
    {
        [SerializeField] private XrSessionController _session;
        [SerializeField] private Camera _camera;
        [SerializeField] private float _distance = 20f;

        private Transform _surface;
        private Material _material;

        private void Awake()
        {
            if (_session == null) _session = FindAnyObjectByType<XrSessionController>();
            if (_camera == null) _camera = GetComponent<Camera>() ?? Camera.main;
        }

        private void Start()
        {
            if (_camera == null) return;
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Rear Camera Preview";
            quad.transform.SetParent(_camera.transform, false);
            Destroy(quad.GetComponent<Collider>());
            _surface = quad.transform;
            Shader shader = Shader.Find("Unlit/Texture");
            if (shader != null)
            {
                _material = new Material(shader) { renderQueue = 1000 };
                quad.GetComponent<MeshRenderer>().sharedMaterial = _material;
            }
            Resize();
        }

        private void Update()
        {
            if (!(_session?.Adapter is PhoneOnlyAdapter phone) || _material == null) return;
            if (_material.mainTexture != phone.PreviewTexture)
                _material.mainTexture = phone.PreviewTexture;
            _surface.localRotation = Quaternion.Euler(0f, 0f, -phone.PreviewRotationAngle);
            _material.mainTextureScale = new Vector2(1f, phone.PreviewVerticallyMirrored ? -1f : 1f);
            _material.mainTextureOffset = new Vector2(0f, phone.PreviewVerticallyMirrored ? 1f : 0f);
            Resize();
        }

        private void Resize()
        {
            if (_camera == null || _surface == null) return;
            float distance = Mathf.Max(_camera.nearClipPlane + 0.1f, _distance);
            float height = 2f * distance * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            _surface.localPosition = new Vector3(0f, 0f, distance);
            _surface.localScale = new Vector3(height * _camera.aspect, height, 1f);
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
