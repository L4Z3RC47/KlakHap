using UnityEngine;

namespace Klak.Hap
{
    [AddComponentMenu("Klak/HAP/HAP Player")]
    public sealed class HapPlayer : MonoBehaviour
    {
        #region Editable attributes

        [SerializeField] string _fileName = "Test.mov";
        [SerializeField] float _time = 0;
        [SerializeField, Range(-10, 10)] float _speed = 1;

        [SerializeField] RenderTexture _targetTexture;
        [SerializeField] Renderer _targetRenderer = null;
        [SerializeField] string _targetMaterialProperty = null;

        #endregion

        #region Private members

        Demuxer _demuxer;
        StreamReader _stream;
        Decoder _decoder;
        TextureUpdater _updater;

        Texture2D _texture;

        float _playbackTime;
        float _appliedSpeed;

        Material _blitMaterial;
        MaterialPropertyBlock _propertyBlock;

        #endregion

        #region MonoBehaviour implementation

        void Start()
        {
            // Demuxer instantiation
            var path = System.IO.Path.Combine(Application.streamingAssetsPath, _fileName);
            _demuxer = new Demuxer(path);

            if (!_demuxer.IsValid)
            {
                _demuxer.Dispose();
                _demuxer = null;
                return;
            }

            // Stream reader instantiation
            _stream = new StreamReader(_demuxer, _time, _speed / 60);
            _playbackTime = _time;
            _appliedSpeed = _speed;

            // Decoder instantiation
            _decoder = new Decoder(
                _stream, _demuxer.Width, _demuxer.Height, _demuxer.VideoType
            );

            // Texture initialization
            _texture = new Texture2D(
                _demuxer.Width, _demuxer.Height, _demuxer.TextureFormat, false
            );
            _updater = new TextureUpdater(_texture, _decoder.CallbackID);
        }

        void OnDestroy()
        {
            if (_updater != null)
            {
                _updater.Dispose();
                _updater = null;
            }

            if (_decoder != null)
            {
                _decoder.Dispose();
                _decoder = null;
            }

            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }

            if (_demuxer != null)
            {
                _demuxer.Dispose();
                _demuxer = null;
            }

            if (_texture != null)
            {
                Destroy(_texture);
                _texture = null;
            }

            if (_blitMaterial != null)
            {
                Destroy(_blitMaterial);
                _blitMaterial = null;
            }
        }

        void Update()
        {
            if (_demuxer == null) return;

            // Restart the stream reader when the time/speed were changed.
            if (_time != _playbackTime || _speed != _appliedSpeed)
            {
                _stream.Restart(_time, _speed / 60);
                _playbackTime = _time;
                _appliedSpeed = _speed;
            }

            _decoder.UpdateTime(_time);
            _updater.RequestUpdate();

            _time += Time.deltaTime * _appliedSpeed;
            _playbackTime = _time;

            // Renderer override
            if (_targetRenderer != null)
            {
                // Material property block lazy initialization
                if (_propertyBlock == null)
                    _propertyBlock = new MaterialPropertyBlock();

                // Read-modify-write
                _targetRenderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetTexture(_targetMaterialProperty, _texture);
                _targetRenderer.SetPropertyBlock(_propertyBlock);
            }
        }

        #endregion
    }
}
