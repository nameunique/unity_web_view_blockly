(function () {
  'use strict';

  var workspace;
  var interpreter = null;
  var running = false;
  var paused = false;
  var aborted = false;
  var lastJavaScriptCode = '';
  var lastPythonCode = '';
  var highlightedBlockId = null;
  var unityReady = false;
  var updateCodePreviewTimer = null;
  var enableBlockHighlight = false;
  var nextStepDelayMs = 0;

  var jsGenerator = Blockly.JavaScript || (window.javascript && window.javascript.javascriptGenerator);
  var pythonGenerator = Blockly.Python || (window.python && window.python.pythonGenerator);

  var statusElement = document.getElementById('status');
  var startButton = document.getElementById('startButton');
  var pauseButton = document.getElementById('pauseButton');
  var abortButton = document.getElementById('abortButton');
  var pythonCodeElement = document.getElementById('pythonCode');
  var javascriptCodeElement = document.getElementById('javascriptCode');
  var dialogOverlay = document.getElementById('appDialogOverlay');
  var dialogForm = document.getElementById('appDialog');
  var dialogMessage = document.getElementById('appDialogMessage');
  var dialogInput = document.getElementById('appDialogInput');
  var dialogCancelButton = document.getElementById('appDialogCancel');
  var dialogOkButton = document.getElementById('appDialogOk');
  var activeDialogCallback = null;
  var activeDialogMode = null;

  window.UnityRuntime = {
    setUnityReady: function (ready) {
      unityReady = !!ready;
      setStatus(unityReady ? 'Unity bridge ready' : 'Unity bridge offline');
    },
    onUnityEvent: function (eventName, payload) {
      if (eventName === 'cubeSpeed') {
        setStatus('скорость куба: ' + payload);
        return;
      }

      if (eventName === 'status') {
        setStatus(payload);
      }
    }
  };

  function setStatus(text) {
    statusElement.textContent = text;
  }

  function configureBlocklyDialogs() {
    if (!Blockly.dialog) {
      return;
    }

    if (typeof Blockly.dialog.setPrompt === 'function') {
      Blockly.dialog.setPrompt(showPromptDialog);
    }

    if (typeof Blockly.dialog.setAlert === 'function') {
      Blockly.dialog.setAlert(showAlertDialog);
    }

    if (typeof Blockly.dialog.setConfirm === 'function') {
      Blockly.dialog.setConfirm(showConfirmDialog);
    }
  }

  function showPromptDialog(message, defaultValue, callback) {
    openDialog({
      mode: 'prompt',
      message: message,
      value: defaultValue || '',
      showInput: true,
      showCancel: true,
      callback: callback
    });
  }

  function showAlertDialog(message, callback) {
    openDialog({
      mode: 'alert',
      message: message,
      value: '',
      showInput: false,
      showCancel: false,
      callback: function () {
        if (callback) {
          callback();
        }
      }
    });
  }

  function showConfirmDialog(message, callback) {
    openDialog({
      mode: 'confirm',
      message: message,
      value: '',
      showInput: false,
      showCancel: true,
      callback: callback
    });
  }

  function openDialog(options) {
    closeDialog(null);

    activeDialogMode = options.mode;
    activeDialogCallback = options.callback || function () {};
    dialogMessage.textContent = options.message || '';
    dialogInput.value = options.value || '';
    dialogInput.hidden = !options.showInput;
    dialogCancelButton.hidden = !options.showCancel;
    dialogOverlay.hidden = false;

    window.setTimeout(function () {
      if (options.showInput) {
        dialogInput.focus();
        dialogInput.select();
      } else {
        dialogOkButton.focus();
      }
    }, 0);
  }

  function closeDialog(result) {
    if (!dialogOverlay || dialogOverlay.hidden) {
      return;
    }

    dialogOverlay.hidden = true;
    dialogInput.value = '';

    var callback = activeDialogCallback;
    activeDialogCallback = null;
    activeDialogMode = null;

    if (callback) {
      callback(result);
    }
  }

  function acceptDialog() {
    if (activeDialogMode === 'prompt') {
      closeDialog(dialogInput.value);
      return;
    }

    if (activeDialogMode === 'confirm') {
      closeDialog(true);
      return;
    }

    closeDialog();
  }

  function cancelDialog() {
    if (activeDialogMode === 'confirm') {
      closeDialog(false);
      return;
    }

    closeDialog(null);
  }

  function createBlocklyTheme() {
    return Blockly.Theme.defineTheme('unity_workspace_theme', {
      base: Blockly.Themes.Classic,
      blockStyles: {
        logic_blocks: {
          colourPrimary: '#4c8fd8',
          colourSecondary: '#3f79b9',
          colourTertiary: '#35679e'
        },
        loop_blocks: {
          colourPrimary: '#5bb85d',
          colourSecondary: '#48994a',
          colourTertiary: '#3f843f'
        },
        math_blocks: {
          colourPrimary: '#4b57b8',
          colourSecondary: '#3f489a',
          colourTertiary: '#363d84'
        },
        text_blocks: {
          colourPrimary: '#42a777',
          colourSecondary: '#368c64',
          colourTertiary: '#2f7956'
        },
        list_blocks: {
          colourPrimary: '#7445a5',
          colourSecondary: '#60398b',
          colourTertiary: '#512f76'
        },
        variable_blocks: {
          colourPrimary: '#a65383',
          colourSecondary: '#8c456f',
          colourTertiary: '#74385c'
        },
        procedure_blocks: {
          colourPrimary: '#9747a7',
          colourSecondary: '#7f3c8c',
          colourTertiary: '#6c3277'
        },
        cube_blocks: {
          colourPrimary: '#b76072',
          colourSecondary: '#9c5061',
          colourTertiary: '#81414f'
        }
      },
      categoryStyles: {
        logic_category: { colour: '#4c8fd8' },
        loop_category: { colour: '#5bb85d' },
        math_category: { colour: '#4b57b8' },
        text_category: { colour: '#42a777' },
        list_category: { colour: '#7445a5' },
        variable_category: { colour: '#a65383' },
        procedure_category: { colour: '#9747a7' },
        cube_category: { colour: '#b76072' }
      },
      componentStyles: {
        workspaceBackgroundColour: 'rgba(255, 255, 255, 0.2)',
        toolboxBackgroundColour: '#1d1d1d',
        toolboxForegroundColour: '#ffffff',
        flyoutBackgroundColour: '#3a3a3a',
        flyoutForegroundColour: '#ffffff',
        flyoutOpacity: 1,
        scrollbarColour: '#c7c7c7',
        scrollbarOpacity: 0.9,
        insertionMarkerColour: '#ffffff',
        insertionMarkerOpacity: 0.3,
        markerColour: '#ffffff',
        cursorColour: '#ffffff'
      }
    });
  }

  function applyWorkspaceBackground() {
    window.setTimeout(function () {
      var svg = document.querySelector('#blocklyDiv .blocklySvg');
      var background = document.querySelector('#blocklyDiv .blocklyMainBackground');

      if (svg) {
        svg.style.backgroundColor = 'rgba(255, 255, 255, 0.2)';
      }

      if (background) {
        background.style.opacity = '0.2';
        background.style.fill = '#ffffff';
      }
    }, 0);
  }

  function defineCustomBlocks() {
    Blockly.defineBlocksWithJsonArray([
      {
        type: 'cube_set_rotation_speed',
        message0: 'задать скорость вращения кубу %1',
        args0: [
          {
            type: 'field_number',
            name: 'SPEED',
            value: 0.5,
            min: -20,
            max: 20,
            precision: 0.1
          }
        ],
        previousStatement: null,
        nextStatement: null,
        style: 'cube_blocks',
        tooltip: 'Передаёт скорость вращения куба в Unity.',
        helpUrl: ''
      },
      {
        type: 'cube_rotate_for',
        message0: 'вращать куб с скоростью %1 и времени %2 секунду',
        args0: [
          {
            type: 'field_number',
            name: 'SPEED',
            value: 0.5,
            min: -20,
            max: 20,
            precision: 0.1
          },
          {
            type: 'field_number',
            name: 'SECONDS',
            value: 1,
            min: 0,
            precision: 0.1
          }
        ],
        previousStatement: null,
        nextStatement: null,
        style: 'cube_blocks',
        tooltip: 'Вращает куб с указанной скоростью заданное количество секунд.',
        helpUrl: ''
      }
    ]);

    jsGenerator.forBlock.cube_set_rotation_speed = function (block) {
      var speed = Number(block.getFieldValue('SPEED') || 0);
      return 'setCubeRotationSpeed(' + speed + ');\n';
    };

    pythonGenerator.forBlock.cube_set_rotation_speed = function (block) {
      var speed = Number(block.getFieldValue('SPEED') || 0);
      return 'cube.set_rotation_speed(' + speed + ')\n';
    };

    jsGenerator.forBlock.cube_rotate_for = function (block) {
      var speed = Number(block.getFieldValue('SPEED') || 0);
      var seconds = Number(block.getFieldValue('SECONDS') || 0);
      return 'rotateCubeFor(' + speed + ', ' + seconds + ');\n';
    };

    pythonGenerator.forBlock.cube_rotate_for = function (block) {
      var speed = Number(block.getFieldValue('SPEED') || 0);
      var seconds = Number(block.getFieldValue('SECONDS') || 0);
      return 'cube.rotate_for(' + speed + ', ' + seconds + ')\n';
    };

    jsGenerator.forBlock.text_print = function (block, generator) {
      var orderNone = jsGenerator.ORDER_NONE ||
        (window.javascript && window.javascript.Order && window.javascript.Order.NONE) ||
        0;
      var text = generator.valueToCode(block, 'TEXT', orderNone) || "''";
      return 'log(' + text + ');\n';
    };
  }

  function createWorkspace() {
    workspace = Blockly.inject('blocklyDiv', {
      toolbox: document.getElementById('toolbox'),
      theme: createBlocklyTheme(),
      trashcan: true,
      move: {
        scrollbars: true,
        drag: true,
        wheel: true
      },
      grid: {
        spacing: 20,
        length: 3,
        colour: '#d2d2d2',
        snap: true
      },
      zoom: {
        controls: true,
        wheel: true,
        startScale: 1.15,
        maxScale: 3,
        minScale: 0.4,
        scaleSpeed: 1.2
      }
    });

    applyWorkspaceBackground();

    var defaultXml = '<xml xmlns="https://developers.google.com/blockly/xml">' +
      '<block type="cube_rotate_for" x="24" y="24">' +
      '<field name="SPEED">0.5</field>' +
      '<field name="SECONDS">1</field>' +
      '</block>' +
      '</xml>';

    Blockly.Xml.domToWorkspace(textToDom(defaultXml), workspace);
    workspace.addChangeListener(scheduleCodePreviewUpdate);
    updateCodePreview();
  }

  function textToDom(text) {
    if (Blockly.utils && Blockly.utils.xml && Blockly.utils.xml.textToDom) {
      return Blockly.utils.xml.textToDom(text);
    }

    return new DOMParser().parseFromString(text, 'text/xml').documentElement;
  }

  function updateCodePreview() {
    lastJavaScriptCode = jsGenerator.workspaceToCode(workspace);
    lastPythonCode = pythonGenerator.workspaceToCode(workspace);

    if (javascriptCodeElement) {
      javascriptCodeElement.textContent = lastJavaScriptCode || '// нет JavaScript-кода';
    }

    if (pythonCodeElement) {
      pythonCodeElement.textContent = lastPythonCode || '# нет Python-кода';
    }
  }

  function scheduleCodePreviewUpdate(event) {
    if (!pythonCodeElement && !javascriptCodeElement) {
      return;
    }

    if (event && event.isUiEvent) {
      return;
    }

    if (updateCodePreviewTimer !== null) {
      window.clearTimeout(updateCodePreviewTimer);
    }

    updateCodePreviewTimer = window.setTimeout(function () {
      updateCodePreviewTimer = null;
      updateCodePreview();
    }, 100);
  }

  function postUnity(method, args) {
    args = Array.isArray(args) ? args : [];
    sendUnityMessage(method, args);
  }

  function sendUnityMessage(method, args) {
    if (window.UnityBridge && typeof window.UnityBridge.invoke === 'function') {
      return window.UnityBridge.invoke(method, args);
    }

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

  function highlightBlock(id) {
    if (!enableBlockHighlight) {
      return;
    }

    highlightedBlockId = id || null;
    workspace.highlightBlock(highlightedBlockId);
  }

  function initInterpreterApi(jsInterpreter, globalObject) {
    jsInterpreter.setProperty(
      globalObject,
      'highlightBlock',
      jsInterpreter.createNativeFunction(function (id) {
        highlightBlock(String(id || ''));
      })
    );

    jsInterpreter.setProperty(
      globalObject,
      'setCubeRotationSpeed',
      jsInterpreter.createNativeFunction(function (speed) {
        var nativeSpeed = Number(jsInterpreter.pseudoToNative(speed));
        if (!Number.isFinite(nativeSpeed)) {
          nativeSpeed = 0;
        }

        postUnity('setCubeRotationSpeed', [nativeSpeed]);
      })
    );

    jsInterpreter.setProperty(
      globalObject,
      'waitForSeconds',
      jsInterpreter.createAsyncFunction(function (seconds, callback) {
        var delay = Math.max(0, Number(jsInterpreter.pseudoToNative(seconds)) || 0);
        window.setTimeout(callback, delay * 1000);
      })
    );

    jsInterpreter.setProperty(
      globalObject,
      'rotateCubeFor',
      jsInterpreter.createAsyncFunction(function (speed, seconds, callback) {
        var nativeSpeed = Number(jsInterpreter.pseudoToNative(speed));
        var nativeSeconds = Number(jsInterpreter.pseudoToNative(seconds));

        if (!Number.isFinite(nativeSpeed)) {
          nativeSpeed = 0;
        }

        if (!Number.isFinite(nativeSeconds)) {
          nativeSeconds = 0;
        }

        postUnity('setCubeRotationSpeed', [nativeSpeed]);
        window.setTimeout(function () {
          if (!aborted) {
            postUnity('stopCubeRotation', []);
          }
          callback();
        }, Math.max(0, nativeSeconds) * 1000);
      })
    );

    jsInterpreter.setProperty(
      globalObject,
      'log',
      jsInterpreter.createNativeFunction(function (value) {
        postUnity('logFromJs', [String(jsInterpreter.pseudoToNative(value))]);
      })
    );

    var alertFunction = jsInterpreter.createNativeFunction(function (value) {
      postUnity('logFromJs', [String(jsInterpreter.pseudoToNative(value))]);
    });

    jsInterpreter.setProperty(globalObject, 'alert', alertFunction);
  }

  function startProgram() {
    if (running && paused) {
      resumeProgram();
      return;
    }

    if (running) {
      return;
    }

    updateCodePreview();
    postUnity('onPythonGenerated', [lastPythonCode]);
    postUnity('resumeCube', []);

    jsGenerator.STATEMENT_PREFIX = enableBlockHighlight ? 'highlightBlock(%1);\n' : '';
    jsGenerator.addReservedWords('highlightBlock,setCubeRotationSpeed,rotateCubeFor,waitForSeconds,log');
    lastJavaScriptCode = jsGenerator.workspaceToCode(workspace);
    if (javascriptCodeElement) {
      javascriptCodeElement.textContent = lastJavaScriptCode || '// нет JavaScript-кода';
    }

    try {
      interpreter = new Interpreter(lastJavaScriptCode, initInterpreterApi);
    } catch (error) {
      setStatus('ошибка интерпретатора');
      console.error(error);
      return;
    }

    running = true;
    paused = false;
    aborted = false;
    startButton.textContent = 'Старт';
    pauseButton.textContent = 'Пауза';
    setStatus(unityReady ? 'выполняется' : 'выполняется, Unity bridge offline');
    runNextStep();
  }

  function runNextStep() {
    if (!running || paused || aborted || !interpreter) {
      return;
    }

    var hasMoreCode = false;
    try {
      hasMoreCode = interpreter.run();
    } catch (error) {
      running = false;
      paused = false;
      workspace.highlightBlock(null);
      setStatus('ошибка: ' + error);
      postUnity('logFromJs', ['Interpreter error: ' + error]);
      return;
    }

    if (hasMoreCode) {
      window.setTimeout(runNextStep, nextStepDelayMs);
      return;
    }

    running = false;
    paused = false;
    interpreter = null;
    workspace.highlightBlock(null);
    setStatus('завершено');
  }

  function pauseProgram() {
    if (!running) {
      return;
    }

    paused = !paused;
    if (paused) {
      postUnity('pauseCube', []);
      pauseButton.textContent = 'Продолжить';
      setStatus('пауза');
      return;
    }

    resumeProgram();
  }

  function resumeProgram() {
    if (!running || !paused) {
      return;
    }

    paused = false;
    postUnity('resumeCube', []);
    pauseButton.textContent = 'Пауза';
    setStatus('выполняется');
    runNextStep();
  }

  function abortProgram() {
    aborted = true;
    running = false;
    paused = false;
    interpreter = null;
    pauseButton.textContent = 'Пауза';
    workspace.highlightBlock(null);
    postUnity('abortCube', []);
    setStatus('остановлено');
  }

  function bindUi() {
    startButton.addEventListener('click', startProgram);
    pauseButton.addEventListener('click', pauseProgram);
    abortButton.addEventListener('click', abortProgram);

    dialogForm.addEventListener('submit', function (event) {
      event.preventDefault();
      acceptDialog();
    });

    dialogCancelButton.addEventListener('click', cancelDialog);

    dialogOverlay.addEventListener('mousedown', function (event) {
      if (event.target === dialogOverlay) {
        cancelDialog();
      }
    });

    document.addEventListener('keydown', function (event) {
      if (!dialogOverlay.hidden && event.key === 'Escape') {
        event.preventDefault();
        cancelDialog();
      }
    });
  }

  function init() {
    if (!jsGenerator || !pythonGenerator) {
      setStatus('генераторы Blockly не загружены');
      return;
    }

    configureBlocklyDialogs();
    defineCustomBlocks();
    createWorkspace();
    bindUi();
    setStatus('готово');
  }

  window.addEventListener('load', init);
})();
