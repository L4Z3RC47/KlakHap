using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Klak.Hap
{
    [ExecuteInEditMode, AddComponentMenu("Klak/HAP/HAP Player")]
    public sealed class HapPlayer : MonoBehaviour, ITimeControl, IPropertyPreview
    {
        #region Editable attributes

        public enum PathMode { StreamingAssets, LocalFileSystem }

        [SerializeField] PathMode _pathMode = PathMode.StreamingAssets;
        [SerializeField] string _filePath = "";

        [SerializeField] float _time = 0;
        [SerializeField, Range(-10, 10)] float _speed = 1;
        [SerializeField] bool _loop = true;

        [SerializeField] RenderTexture _targetTexture = null;
        [SerializeField] Renderer _targetRenderer = null;
        [SerializeField] string _targetMaterialProperty = "_MainTex";

        #endregion

        #region Public properties

        public float time {
            get { return _time; }
            set { _time = value; }
        }

        public float speed {
            get { return _speed; }
            set { _speed = value; }
        }

        public bool loop {
            get { return _loop; }
            set { _loop = value; }
        }

        public RenderTexture targetTexture {
            get { return _targetTexture; }
            set { _targetTexture = value; }
        }

        public Renderer targetRenderer {
            get { return _targetRenderer; }
            set { _targetRenderer = value; }
        }

        public string targetMaterialProperty {
            get { return _targetMaterialProperty; }
            set { _targetMaterialProperty = value; }
        }

        #endregion

        #region Read-only properties

        public bool isValid { get { return _demuxer != null; } }
        public int frameWidth { get { return _demuxer?.Width ?? 0; } }
        public int frameHeight { get { return _demuxer?.Height ?? 0; } }
        public int frameCount { get { return _demuxer?.FrameCount ?? 0; } }
        public double streamDuration { get { return _demuxer?.Duration ?? 0; } }

        public CodecType codecType { get {
            return Utility.DetermineCodecType(_demuxer?.VideoType ?? 0);
        } }

        public string resolvedFilePath { get {
            if (_pathMode == PathMode.StreamingAssets)
                return System.IO.Path.Combine(Application.streamingAssetsPath, _filePath);
            else
                return _filePath;
        } }

        public Texture2D texture { get { return _texture; } }

        #endregion

        #region Public methods

        public void Open(string filePath, PathMode pathMode = PathMode.StreamingAssets)
        {
            if (_demuxer != null)
            {
                Debug.LogError("Stream has already been opened.");
                return;
            }

            _filePath = filePath;
            _pathMode = pathMode;

            OpenInternal();
        }

        #endregion

        #region Private members

        Demuxer _demuxer;
        StreamReader _stream;
        Decoder _decoder;

        Texture2D _texture;
        TextureUpdater _updater;

        float _storedTime;
        float _storedSpeed;

        void OpenInternal()
        {
            // Demuxer instantiation
            _demuxer = new Demuxer(resolvedFilePath);

            if (!_demuxer.IsValid)
            {
                if (Application.isPlaying)
                {
                    // In play mode, show an error message and disable itself.
                    Debug.LogError("Failed to open stream (" + resolvedFilePath + ").");
                    enabled = false;
                }
                _demuxer.Dispose();
                _demuxer = null;
                return;
            }

            // Stream reader instantiation
            _stream = new StreamReader(_demuxer, _time, _speed / 60);
            (_storedTime, _storedSpeed) = (_time, _speed);

            // Decoder instantiation
            _decoder = new Decoder(
                _stream, _demuxer.Width, _demuxer.Height, _demuxer.VideoType
            );

            // Texture initialization
            _texture = new Texture2D(
                _demuxer.Width, _demuxer.Height,
                Utility.DetermineTextureFormat(_demuxer.VideoType), false
            );
            _texture.wrapMode = TextureWrapMode.Clamp;
            _texture.hideFlags = HideFlags.DontSave;

            _updater = new TextureUpdater(_texture, _decoder);

            // Start the updater coroutine if async update is not supported.
            if (!TextureUpdater.AsyncSupport) StartCoroutine(DelayedUpdater());
        }

        System.Collections.IEnumerator DelayedUpdater()
        {
            for (var eof = new WaitForEndOfFrame(); enabled;)
            {
                _updater.UpdateNow();
                yield return eof;
            }
        }

        #endregion

        #region External object updaters

        Material _blitMaterial;
        MaterialPropertyBlock _propertyBlock;

        void UpdateTargetTexture()
        {
            if (_targetTexture == null) return;

            // Material lazy initialization
            if (_blitMaterial == null)
            {
                _blitMaterial = new Material(Utility.DetermineShader(_demuxer.VideoType));
                _blitMaterial.hideFlags = HideFlags.DontSave;
            }

            // Blit
            Graphics.Blit(_texture, _targetTexture, _blitMaterial, 0);
        }

        void UpdateTargetRenderer()
        {
            if (_targetRenderer == null) return;

            // Material property block lazy initialization
            if (_propertyBlock == null)
                _propertyBlock = new MaterialPropertyBlock();

            // Read-modify-write
            _targetRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetTexture(_targetMaterialProperty, _texture);
            _targetRenderer.SetPropertyBlock(_propertyBlock);
        }

        #endregion

        #region ITimeControl functions

        float _externalTime = -1;

        public void OnControlTimeStart()
        {
            _externalTime = 0;
        }

        public void OnControlTimeStop()
        {
            _externalTime = -1;
        }

        public void SetTime(double time)
        {
            _externalTime = (float)time;
        }

        #endregion

        #region IPropertyPreview implementation

        public void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
        }

        #endregion

        #region MonoBehaviour implementation

        void OnEnable()
        {
            // Updater coroutine restart
            if (_updater != null && !TextureUpdater.AsyncSupport)
                StartCoroutine(DelayedUpdater());
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

            Utility.Destroy(_texture);
            Utility.Destroy(_blitMaterial);
        }

        void LateUpdate()
        {
            // Lazy initialization of demuxer
            if (_demuxer == null && !string.IsNullOrEmpty(_filePath))
                OpenInternal();

            // Do nothing if the demuxer hasn't been instantiated.
            if (_demuxer == null) return;

            float t, dt;

            if (_externalTime < 0)
            {
                // Internal time mode
                t = _time;
                dt = _speed / 60;
            }
            else
            {
                // External time (timeline) mode
                t = _externalTime;
                dt = (_externalTime > _storedTime ? 1 : -1) * _speed / 60;
            }

            // Non-loop time clamping
            if (!_loop) t = Mathf.Clamp(t, 0, (float)_demuxer.Duration);

            // Were time/speed externally modified?
            if (t != _storedTime || _speed != _storedSpeed)
            {
                // Restart the stream reader.
                _stream.Restart(t, dt);

                // Update the decoder time, then wait for deocoding.
                _decoder.UpdateTime(t, true);
            }
            else
            {
                // Update the decoder time asynchronously.
                _decoder.UpdateTime(t);
            }

            // Automatically advance the time in the play mode.
            if (Application.isPlaying) t += Time.deltaTime * _speed;
            _time = t;

            // Update the stored state.
            (_storedTime, _storedSpeed) = (t, _speed);

            // Async texture update request
            if (TextureUpdater.AsyncSupport) _updater.RequestAsyncUpdate();

            // External object updates
            UpdateTargetRenderer();
            UpdateTargetTexture();
        }

        #endregion
    }
}
