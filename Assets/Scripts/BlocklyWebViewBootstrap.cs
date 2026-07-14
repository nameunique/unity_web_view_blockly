using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VoltstroStudios.UnityWebBrowser;
using VoltstroStudios.UnityWebBrowser.Communication;
using VoltstroStudios.UnityWebBrowser.Core.Engines;
using VoltstroStudios.UnityWebBrowser.Input;
using VoltstroStudios.UnityWebBrowser.Shared;
using VoltstroStudios.UnityWebBrowser.Shared.Core;
using UwbResolution = VoltstroStudios.UnityWebBrowser.Shared.Resolution;

#if UNITY_EDITOR
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
#endif

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

[ExecuteAlways]
public sealed class BlocklyWebViewBootstrap : MonoBehaviour
{
    private const string BlocklyRelativePath = "Blockly/index.html";
    private const string EditorPreviewCanvasName = "Blockly WebView Canvas (Editor Preview)";
    private const float WebViewWidthRatio = 0.6f;
    private const int BrowserFrameRate = 60;
    private const bool LogBridgeMessages = false;

    private WebBrowserUIBasic webBrowser;
    private BlocklyCubeController cubeController;
    private readonly HashSet<int> receivedBrowserMessageIds = new();
    private readonly Queue<int> receivedBrowserMessageOrder = new();
    private readonly Queue<Action> mainThreadActions = new();
    private readonly object mainThreadActionsLock = new();
    private int mainThreadId;

    private bool IsMainThread => mainThreadId == 0 || Environment.CurrentManagedThreadId == mainThreadId;

    private void Awake()
    {
        mainThreadId = Environment.CurrentManagedThreadId;
    }

#if UNITY_EDITOR
    private void OnEnable()
    {
        if (!Application.isPlaying)
            RemoveEditorPreviewCanvas();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
            RemoveEditorPreviewCanvas();
    }
#endif

    private void Start()
    {
        if (!Application.isPlaying)
            return;

        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = BrowserFrameRate;

#if UNITY_EDITOR
        RemoveEditorPreviewCanvas();
#endif

        EnsureCamera();
        EnsureLight();
        cubeController = CreateCube();
        CreateEventSystem();
        CreateBlocklyWebView();
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;

        while (true)
        {
            Action action;
            lock (mainThreadActionsLock)
            {
                if (mainThreadActions.Count == 0)
                    return;

                action = mainThreadActions.Dequeue();
            }

            action?.Invoke();
        }
    }

    private static Camera EnsureCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            GameObject cameraObject = new("Main Camera");
            cameraObject.tag = "MainCamera";
            mainCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        mainCamera.transform.position = new Vector3(1.65f, 1.1f, -6f);
        mainCamera.transform.rotation = Quaternion.Euler(10f, -8f, 0f);
        mainCamera.clearFlags = CameraClearFlags.SolidColor;
        mainCamera.backgroundColor = new Color(0.08f, 0.1f, 0.12f);
        mainCamera.fieldOfView = 55f;

        return mainCamera;
    }

    private static void EnsureLight()
    {
        Light existingLight = FindFirstObjectByType<Light>();
        if (existingLight != null)
        {
            existingLight.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            existingLight.intensity = 1.2f;
            return;
        }

        GameObject lightObject = new("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    private static BlocklyCubeController CreateCube()
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Blockly API Cube";
        cube.transform.position = new Vector3(1.65f, 0f, 0f);
        cube.transform.localScale = Vector3.one * 1.35f;

        Renderer renderer = cube.GetComponent<Renderer>();
        Shader shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Diffuse");
        renderer.material = new Material(shader)
        {
            color = new Color(0.1f, 0.62f, 0.95f)
        };

        return cube.AddComponent<BlocklyCubeController>();
    }

    private static void CreateEventSystem()
    {
        EventSystem existingEventSystem = FindFirstObjectByType<EventSystem>();
        if (existingEventSystem != null)
        {
            EnsureEventSystemInputModule(existingEventSystem.gameObject);
            return;
        }

        GameObject eventSystemObject = new("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        EnsureEventSystemInputModule(eventSystemObject);
    }

#if ENABLE_INPUT_SYSTEM
    private static void EnsureEventSystemInputModule(GameObject eventSystemObject)
    {
        InputSystemUIInputModule inputModule = eventSystemObject.GetComponent<InputSystemUIInputModule>();
        if (inputModule == null)
            inputModule = eventSystemObject.AddComponent<InputSystemUIInputModule>();

        inputModule.AssignDefaultActions();
    }
#else
    private static void EnsureEventSystemInputModule(GameObject eventSystemObject)
    {
        if (eventSystemObject.GetComponent<StandaloneInputModule>() == null)
            eventSystemObject.AddComponent<StandaloneInputModule>();
    }
#endif

    private void CreateBlocklyWebView()
    {
        GameObject canvasObject = new("Blockly WebView Canvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject browserObject = new("Blockly WebView");
        browserObject.transform.SetParent(canvasObject.transform, false);
        browserObject.layer = LayerMask.NameToLayer("UI");

        RectTransform rectTransform = browserObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 0f);
        rectTransform.anchorMax = new Vector2(WebViewWidthRatio, 1f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        RawImage rawImage = browserObject.AddComponent<RawImage>();
        rawImage.color = Color.white;
        rawImage.raycastTarget = true;

        webBrowser = browserObject.AddComponent<WebBrowserUIBasic>();
        ConfigureBrowserClient();
    }

    private void ConfigureBrowserClient()
    {
        EngineConfiguration engineConfig = ScriptableObject.CreateInstance<EngineConfiguration>();
        engineConfig.engineAppName = "UnityWebBrowser.Engine.Cef";
        engineConfig.engineFiles = new[]
        {
            CreateEnginePlatformFiles(
                Platform.Windows64,
                "dev.voltstro.unitywebbrowser.engine.cef.win.x64",
                string.Empty,
                "UWB/"),
            CreateEnginePlatformFiles(
                Platform.Linux64,
                "dev.voltstro.unitywebbrowser.engine.cef.linux.x64",
                string.Empty,
                "UWB/"),
            CreateEnginePlatformFiles(
                Platform.MacOS,
                "dev.voltstro.unitywebbrowser.engine.cef.macos.x64",
                "UnityWebBrowser.Engine.Cef.app/Contents/MacOS/",
                "Frameworks/"),
            CreateEnginePlatformFiles(
                Platform.MacOSArm64,
                "dev.voltstro.unitywebbrowser.engine.cef.macos.arm64",
                "UnityWebBrowser.Engine.Cef.app/Contents/MacOS/",
                "Frameworks/")
        };

        webBrowser.browserClient.engine = engineConfig;
        webBrowser.browserClient.communicationLayer = ScriptableObject.CreateInstance<TCPCommunicationLayer>();
        webBrowser.browserClient.initialUrl = GetBlocklyUrl();
        webBrowser.browserClient.javascript = true;
        webBrowser.browserClient.localStorage = true;
        webBrowser.browserClient.backgroundColor = new Color32(0, 0, 0, 0);
        webBrowser.browserClient.windowlessFrameRate = BrowserFrameRate;
        webBrowser.browserClient.jsMethodManager.jsMethodsEnable = true;
        SetInitialBrowserResolution();

        webBrowser.inputHandler = CreateInputHandler();

        webBrowser.browserClient.RegisterJsMethod<double>("setCubeRotationSpeed", SetCubeRotationSpeedFromJs);
        webBrowser.browserClient.RegisterJsMethod("stopCubeRotation", StopCubeRotationFromJs);
        webBrowser.browserClient.RegisterJsMethod("pauseCube", PauseCubeFromJs);
        webBrowser.browserClient.RegisterJsMethod("resumeCube", ResumeCubeFromJs);
        webBrowser.browserClient.RegisterJsMethod("abortCube", AbortCubeFromJs);
        webBrowser.browserClient.RegisterJsMethod<string>("logFromJs", LogFromJs);
        webBrowser.browserClient.RegisterJsMethod<string>("onPythonGenerated", OnPythonGenerated);

        webBrowser.browserClient.OnLoadFinish += OnBrowserLoadFinished;
        webBrowser.browserClient.OnClientConnected += ResizeBrowserToWebView;
        webBrowser.browserClient.OnUrlChanged += OnBrowserUrlChanged;
        webBrowser.browserClient.OnTitleChange += OnBrowserTitleChanged;
    }

    private static Engine.EnginePlatformFiles CreateEnginePlatformFiles(
        Platform platform,
        string packageName,
        string engineBaseAppLocation,
        string engineRuntimeLocation)
    {
        return new Engine.EnginePlatformFiles
        {
            platform = platform,
            engineBaseAppLocation = engineBaseAppLocation,
            engineRuntimeLocation = engineRuntimeLocation
#if UNITY_EDITOR
            , engineEditorLocation = GetEngineEditorLocation(packageName)
#endif
        };
    }

#if UNITY_EDITOR
    private static string GetEngineEditorLocation(string packageName)
    {
        PackageInfo packageInfo = PackageInfo.FindForAssetPath($"Packages/{packageName}/package.json");
        if (packageInfo == null)
            return $"Packages/{packageName}/Engine~/";

        return Path.Combine(packageInfo.resolvedPath, "Engine~") + Path.DirectorySeparatorChar;
    }
#endif

    private void SetInitialBrowserResolution()
    {
        FieldInfo resolutionField = webBrowser.browserClient.GetType()
            .GetField("resolution", BindingFlags.Instance | BindingFlags.NonPublic);

        resolutionField?.SetValue(webBrowser.browserClient, GetWebViewResolution());
    }

    private static UwbResolution GetWebViewResolution()
    {
        uint width = (uint)Mathf.Max(640, Mathf.RoundToInt(Screen.width * WebViewWidthRatio));
        uint height = (uint)Mathf.Max(480, Screen.height);
        return new UwbResolution(width, height);
    }

    private void ResizeBrowserToWebView()
    {
        RunOnMainThread(ResizeBrowserToWebViewOnMainThread);
    }

    private void ResizeBrowserToWebViewOnMainThread()
    {
        try
        {
            webBrowser.browserClient.Resize(GetWebViewResolution());
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Unable to resize Blockly WebView: {ex.Message}");
        }
    }

    private static WebBrowserInputHandler CreateInputHandler()
    {
#if ENABLE_INPUT_SYSTEM
        WebBrowserInputSystemHandler inputHandler = ScriptableObject.CreateInstance<WebBrowserInputSystemHandler>();
        inputHandler.pointPosition = new InputAction("UWB Pointer Position", InputActionType.Value, "<Pointer>/position");
        inputHandler.scrollInput = new InputAction("UWB Mouse Scroll", InputActionType.Value, "<Mouse>/scroll");
        inputHandler.scrollValue = 0.2f;
        inputHandler.pointPosition.Enable();
        inputHandler.scrollInput.Enable();
        return inputHandler;
#else
        return ScriptableObject.CreateInstance<WebBrowserOldInputHandler>();
#endif
    }

    private static string GetBlocklyUrl()
    {
        string indexPath = Path.Combine(Application.streamingAssetsPath, BlocklyRelativePath);
        return new Uri(indexPath).AbsoluteUri;
    }

#if UNITY_EDITOR
    private static void RemoveEditorPreviewCanvas()
    {
        GameObject previewCanvas = GameObject.Find(EditorPreviewCanvasName);
        if (previewCanvas != null)
            DestroyImmediate(previewCanvas);
    }
#endif

    private void OnBrowserLoadFinished(string url)
    {
        RunOnMainThread(() =>
        {
            InjectUnityBridge();
            SendUnityEvent("status", "Unity bridge ready");
        });
    }

    private void OnBrowserUrlChanged(string url)
    {
        RunOnMainThread(() => HandleBrowserUrlChanged(url));
    }

    private void HandleBrowserUrlChanged(string url)
    {
        const string marker = "#unityMessage=";
        int markerIndex = url.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return;

        string encodedMessage = url[(markerIndex + marker.Length)..];
        int nextParameter = encodedMessage.IndexOf('&');
        if (nextParameter >= 0)
            encodedMessage = encodedMessage[..nextParameter];

        TryDispatchBrowserMessage(encodedMessage, "url");
    }

    private void OnBrowserTitleChanged(string title)
    {
        RunOnMainThread(() => HandleBrowserTitleChanged(title));
    }

    private void HandleBrowserTitleChanged(string title)
    {
        const string marker = "unityMessage:";
        int markerIndex = title.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return;

        string encodedMessage = title[(markerIndex + marker.Length)..];
        int nextParameter = encodedMessage.IndexOf('|');
        if (nextParameter >= 0)
            encodedMessage = encodedMessage[..nextParameter];

        TryDispatchBrowserMessage(encodedMessage, "title");
    }

    private void TryDispatchBrowserMessage(string encodedMessage, string source)
    {
        try
        {
            string json = Uri.UnescapeDataString(encodedMessage);
            BrowserMessage message = JsonUtility.FromJson<BrowserMessage>(json);

            if (message == null || string.IsNullOrWhiteSpace(message.method))
                return;

            if (message.id > 0)
            {
                if (!receivedBrowserMessageIds.Add(message.id))
                    return;

                receivedBrowserMessageOrder.Enqueue(message.id);
                while (receivedBrowserMessageOrder.Count > 128)
                    receivedBrowserMessageIds.Remove(receivedBrowserMessageOrder.Dequeue());
            }

            if (LogBridgeMessages)
                Debug.Log($"[Blockly Bridge] {source}: {message.method}({string.Join(", ", message.args ?? Array.Empty<string>())})");

            DispatchBrowserMessage(message);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Unable to dispatch Blockly browser message from {source}: {ex.Message}");
        }
    }

    private void DispatchBrowserMessage(BrowserMessage message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.method))
            return;

        switch (message.method)
        {
            case "setCubeRotationSpeed":
                if (TryReadDouble(message, out double speed))
                    SetCubeRotationSpeedFromJs(speed);
                break;
            case "stopCubeRotation":
                StopCubeRotationFromJs();
                break;
            case "pauseCube":
                PauseCubeFromJs();
                break;
            case "resumeCube":
                ResumeCubeFromJs();
                break;
            case "abortCube":
                AbortCubeFromJs();
                break;
            case "logFromJs":
                LogFromJs(GetFirstArgument(message));
                break;
            case "onPythonGenerated":
                OnPythonGenerated(GetFirstArgument(message));
                break;
        }
    }

    private void SetCubeRotationSpeedFromJs(double speed)
    {
        if (!IsMainThread)
        {
            RunOnMainThread(() => SetCubeRotationSpeedFromJs(speed));
            return;
        }

        cubeController.SetRotationSpeed(speed);
        if (LogBridgeMessages)
            Debug.Log($"[Blockly Cube] Rotation speed set to {cubeController.RotationSpeed.ToString("0.###", CultureInfo.InvariantCulture)}");

        SendUnityEvent("cubeSpeed", cubeController.RotationSpeed.ToString("0.###", CultureInfo.InvariantCulture));
    }

    private void StopCubeRotationFromJs()
    {
        if (!IsMainThread)
        {
            RunOnMainThread(StopCubeRotationFromJs);
            return;
        }

        cubeController.StopRotation();
        SendUnityEvent("cubeSpeed", "0");
    }

    private void PauseCubeFromJs()
    {
        if (!IsMainThread)
        {
            RunOnMainThread(PauseCubeFromJs);
            return;
        }

        cubeController.Pause();
        SendUnityEvent("status", "Cube paused");
    }

    private void ResumeCubeFromJs()
    {
        if (!IsMainThread)
        {
            RunOnMainThread(ResumeCubeFromJs);
            return;
        }

        cubeController.Resume();
        SendUnityEvent("status", "Cube resumed");
    }

    private void AbortCubeFromJs()
    {
        if (!IsMainThread)
        {
            RunOnMainThread(AbortCubeFromJs);
            return;
        }

        cubeController.Abort();
        SendUnityEvent("status", "Cube aborted");
    }

    private void LogFromJs(string message)
    {
        Debug.Log($"[Blockly] {message}");
    }

    private void OnPythonGenerated(string pythonCode)
    {
        Debug.Log($"[Blockly Python]\n{pythonCode}");
    }

    private void RunOnMainThread(Action action)
    {
        if (action == null)
            return;

        if (IsMainThread)
        {
            action();
            return;
        }

        lock (mainThreadActionsLock)
            mainThreadActions.Enqueue(action);
    }

    private void InjectUnityBridge()
    {
        const string bridgeScript = @"
(function () {
  function sendUnityMessage(method, args) {
    args = Array.isArray(args) ? args : [];
    window.__unityBridgeSeq = Number(window.__unityBridgeSeq || 0) + 1;
    var payload = {
      id: window.__unityBridgeSeq,
      method: String(method || ''),
      args: args.map(function (value) { return String(value); })
    };
    var encoded = encodeURIComponent(JSON.stringify(payload));
    document.title = 'unityMessage:' + encoded;
    return true;
  }

  window.UnityBridge = window.UnityBridge || {};
  window.UnityBridge.invoke = sendUnityMessage;
  if (window.UnityRuntime && typeof window.UnityRuntime.setUnityReady === 'function') {
    window.UnityRuntime.setUnityReady(true);
  }
})();";

        ExecuteBrowserJs(bridgeScript);
    }

    private void SendUnityEvent(string eventName, string payload)
    {
        string script = "window.UnityRuntime && window.UnityRuntime.onUnityEvent("
            + JsQuote(eventName) + ", " + JsQuote(payload) + ");";

        ExecuteBrowserJs(script);
    }

    private void ExecuteBrowserJs(string script)
    {
        if (webBrowser == null ||
            !webBrowser.browserClient.ReadySignalReceived ||
            !webBrowser.browserClient.IsConnected)
        {
            return;
        }

        try
        {
            webBrowser.browserClient.ExecuteJs(script);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Unable to execute browser JS: {ex.Message}");
        }
    }

    private static string JsQuote(string value)
    {
        value ??= string.Empty;
        return "'" + value
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n") + "'";
    }

    private static bool TryReadDouble(BrowserMessage message, out double value)
    {
        string firstArgument = GetFirstArgument(message);
        return double.TryParse(firstArgument, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string GetFirstArgument(BrowserMessage message)
    {
        return message.args is { Length: > 0 } ? message.args[0] : string.Empty;
    }

    [Serializable]
    private sealed class BrowserMessage
    {
        public int id = 0;
        public string method = string.Empty;
        public string[] args = Array.Empty<string>();
    }
}
