using UnityEngine;
using UnityEngine.InputSystem.XR;

// RGB-D rendering adapted from https://samarth-robo.github.io/blog/2021/12/28/unity_rgbd_rendering.html

namespace UserInTheBox
{
    public class SimulatedUser : MonoBehaviour
    {
        public Transform leftHandController, rightHandController;
        public Camera mainCamera;
        public RLEnv env;
        private ZmqServer _server;
        private string _port;
        private Rect _rect;
        private RenderTexture _renderTexture;
        private RenderTexture _lightMap;
        private Texture2D _tex;
        private bool _sendReply;
        private byte[] _previousImage;
        [SerializeField] private bool simulated;

        public void Awake()
        {
            // Get server port
            _port = UitBUtils.GetOptionalKeywordArgument("port", "5555");

            // Check if simulated user is enabled
            enabled = UitBUtils.GetOptionalArgument("simulated") | simulated;

            if (enabled)
            {
                // Disable camera; we will call the rendering manually
                mainCamera.enabled = false;
                
                // Make sure depth is rendered/stored, and attach a component to camera to handle RGB-D rendering
                mainCamera.depthTextureMode = DepthTextureMode.Depth;
                mainCamera.gameObject.AddComponent<RenderShader>();

                // Set camera parameters, such as field of view, near/far clipping planes
                mainCamera.fieldOfView = 90;
                mainCamera.nearClipPlane = 0.01f;
                mainCamera.farClipPlane = 10;

                // Disable the TrackedPoseDriver as well, otherwise XR Origin will always
                // try to reset position of camera to (0,0,0)?
                if (mainCamera.GetComponent<TrackedPoseDriver>() != null)
                {
                    mainCamera.GetComponent<TrackedPoseDriver>().enabled = false;
                }
            }
            else
            {
                // If SimulatedUser is not enabled, deactivate it and all its children
                gameObject.SetActive(false);
            }
        }

        public void Start()
        {
            // Check whether default debug port is used
            int timeOutSeconds;
            if (_port == "5555")
            {
                timeOutSeconds = 600;
            }
            else
            {
                timeOutSeconds = 60;
            }
            
            // Initialise ZMQ server
            _server = new ZmqServer(_port, timeOutSeconds);
            
            // Wait for handshake from user-in-the-box simulated user
            var timeOptions = _server.WaitForHandshake();
            
            // Try to run the simulations as fast as possible
            Time.timeScale = timeOptions.timeScale; // Use an integer here!

            // Set policy query frequency
            Application.targetFrameRate = timeOptions.sampleFrequency*(int)Time.timeScale;
        
            // If fixedDeltaTime is defined in timeOptions use it, otherwise use timestep
            Time.fixedDeltaTime = timeOptions.fixedDeltaTime > 0 ? timeOptions.fixedDeltaTime : timeOptions.timestep;

            // Set maximum delta time
            Time.maximumDeltaTime = 1.0f / Application.targetFrameRate;
            
            Screen.SetResolution(1, 1, false);
            // TODO need to make sure width and height are set correctly (read from some global config etc)
            const int width = 120;
            const int height = 80;
            _rect = new Rect(0, 0, width, height);

            // Create render texture, and make camera render into it
            _renderTexture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGBHalf);
            
            // Create 2D texture into which we copy from render texture
            _tex = new Texture2D(width, height, TextureFormat.RGBAHalf, false);
            
            // Need this stupid hack to make rendered textures lighter (see answer by Invertex in
            // https://forum.unity.com/threads/writting-to-rendertexture-comes-out-darker.427631/)
            _lightMap = new RenderTexture(width, height, 16);
            _lightMap.name = "stupid_hack";
            _lightMap.enableRandomWrite = true;
            _lightMap.Create();
        }
        
        public void Update()
        {
            // Get previously received simulation state
            _sendReply = false;
            SimulatedUserState previousState = _server.GetSimulationState();
            
            // Check if we should advance Unity simulation
            if (previousState != null && Time.fixedTime < previousState.nextTimestep)
            {
                return;
            }
            _sendReply = true;

            // Receive state from User-in-the-Box simulation
            var state = _server.ReceiveState();
        
            // Update anchors
            UpdateAnchors(state);

            // Check if we should quit the application, or reset environment
            if (state.quitApplication)
            {
                #if UNITY_EDITOR
                    // Application.Quit() does not work in the editor so
                    // UnityEditor.EditorApplication.isPlaying need to be set to false to end the game
                    UnityEditor.EditorApplication.isPlaying = false;
                #else
                    Application.Quit();
                #endif
            }
            else if (state.reset)
            {
                env.Reset();
            }
        }
        
        private void UpdateAnchors(SimulatedUserState state)
        {
            // Update camera and controller transformations based on the MuJoCo state
            mainCamera.transform.SetPositionAndRotation(state.headsetPosition, state.headsetRotation);
            leftHandController.SetPositionAndRotation(state.leftControllerPosition, state.leftControllerRotation);
            rightHandController.SetPositionAndRotation(state.rightControllerPosition, state.rightControllerRotation);

            if (env.overrideHeadsetOrientation)
            {
                mainCamera.transform.rotation = env.simulatedUserHeadsetOrientation;
            }
        }

        public void LateUpdate()
        {
            // If we didn't receive a state, don't send the observation
            if (!_sendReply)
            {
                return;
            }

            // Get agent camera and manually render scene into renderTexture
            mainCamera.targetTexture = _renderTexture;
            mainCamera.Render();
            
            // ReadPixels will read from the currently active render texture
            RenderTexture.active = _lightMap;
            
            // Do the hack to make image lighter
            Graphics.Blit(_renderTexture, _lightMap);

            // Read pixels from _lightMap into _tex
            _tex.ReadPixels(_rect, 0, 0);

            // Reset active render texture
            RenderTexture.active = null;

            // Encode texture into PNG
            _previousImage = _tex.EncodeToPNG();

            // Get reward
            var reward = env.GetReward();

            // Check if task is finished (terminated by either the app or the simulated user)
            var isFinished = env.IsFinished() || _server.GetSimulationState().isFinished;

            // Get elapsed time (scaled [-1, 1])
            var timeFeature = env.GetTimeFeature();

            // Get dictionary with additional objects to be logged/stored
            var logDict = env.GetLogDict();

            // Send observation to client
            _server.SendObservation(isFinished, reward, _previousImage, timeFeature, logDict);
        }
        
        private void OnDestroy()
        {
            _server?.Close();
        }

        private void OnApplicationQuit()
        {
            _server?.Close();
        }
        
        public string getPort()
        {
            return _port;
        }
    }
}
