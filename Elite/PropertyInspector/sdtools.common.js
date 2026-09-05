// sdtools.common.js v0.8
var websocket = null,
    uuid = null,
    registerEventName = null,
    actionInfo = {},
    inInfo = {},
    runningApps = [],
    isQT = navigator.appVersion.includes('QtWebEngine');

function connectElgatoStreamDeckSocket(inPort, inUUID, inRegisterEvent, inInfo, inActionInfo) {
    uuid = inUUID;
    registerEventName = inRegisterEvent;
    console.log(inUUID, inActionInfo);
    actionInfo = JSON.parse(inActionInfo); // cache the info
    inInfo = JSON.parse(inInfo);
    websocket = new WebSocket('ws://localhost:' + inPort);

    addDynamicStyles(inInfo.colors);

    websocket.onopen = websocketOnOpen;
    websocket.onmessage = websocketOnMessage;

    // Allow others to get notified that the websocket is created
    var event = new Event('websocketCreate');
    document.dispatchEvent(event);

    loadConfiguration(actionInfo.payload.settings);
}

function websocketOnOpen() {
    var json = {
        event: registerEventName,
        uuid: uuid
    };
    websocket.send(JSON.stringify(json));

    // Notify the plugin that we are connected
    sendValueToPlugin('propertyInspectorConnected', 'property_inspector');
}

function websocketOnMessage(evt) {
    // Received message from Stream Deck
    var jsonObj = JSON.parse(evt.data);

    if (jsonObj.event === 'sendToPropertyInspector') {
        var payload = jsonObj.payload;
        loadConfiguration(payload);
    }
    else if (jsonObj.event === 'didReceiveSettings') {
        var payload = jsonObj.payload;
        loadConfiguration(payload.settings);
    }
    else {
        console.log("Unhandled websocketOnMessage: " + jsonObj.event);
    }
}

function loadConfiguration(payload) {
    console.log('loadConfiguration');
    console.log(payload);
    for (var key in payload) {
        try {
            var elem = document.getElementById(key);
            if (elem.classList.contains("sdCheckbox")) { // Checkbox
                elem.checked = payload[key];
            }
            else if (elem.classList.contains("sdFile")) { // File
                var elemFile = document.getElementById(elem.id + "Filename");
                elemFile.innerText = payload[key];
                if (!elemFile.innerText) {
                    elemFile.innerText = "No file...";
                }
            }
            else { // Normal value
                elem.value = payload[key];
            }
            console.log("Load: " + key + "=" + payload[key]);
        }
        catch (err) {
            console.log("loadConfiguration failed for key: " + key + " - " + err);
        }
    }
}

function clearFileName(id) {
    var payload = {};
    var elements = document.getElementsByClassName("sdProperty");

    Array.prototype.forEach.call(elements, function (elem) {
        var key = elem.id;
        if (elem.classList.contains("sdCheckbox")) { // Checkbox
            payload[key] = elem.checked;
        }
        else if (elem.classList.contains("sdFile")) { // File
            var elemFile = document.getElementById(elem.id + "Filename");
            payload[key] = elem.value;
            if (!elem.value) {
                // Fetch innerText if file is empty (happens when we lose and regain focus to this key)
                payload[key] = elemFile.innerText;
            }
            else {
                // Set value on initial file selection
                elemFile.innerText = elem.value;
            }
        }
        else { // Normal value
            payload[key] = elem.value;
        }
        
        if (key == id)
        {
          elem.value = null;
          payload[key] = null;
          var elemFile = document.getElementById(id + "Filename");
          elemFile.innerText = null;
        }
        
        console.log("Save: " + key + "<=" + payload[key]);
    });
    setSettingsToPlugin(payload);
}

function setSettings() {
    var payload = {};
    var elements = document.getElementsByClassName("sdProperty");

    Array.prototype.forEach.call(elements, function (elem) {
        var key = elem.id;
        if (elem.classList.contains("sdCheckbox")) { // Checkbox
            payload[key] = elem.checked;
        }
        else if (elem.classList.contains("sdFile")) { // File
            var elemFile = document.getElementById(elem.id + "Filename");
            payload[key] = elem.value;
            if (!elem.value) {
                // Fetch innerText if file is empty (happens when we lose and regain focus to this key)
                payload[key] = elemFile.innerText;
            }
            else {
                // Set value on initial file selection
                elemFile.innerText = elem.value;
            }
        }
        else { // Normal value
            payload[key] = elem.value;
        }
        console.log("Save: " + key + "<=" + payload[key]);
    });
    setSettingsToPlugin(payload);
}

function setSettingsToPlugin(payload) {
    if (websocket && (websocket.readyState === 1)) {
        const json = {
            'event': 'setSettings',
            'context': uuid,
            'payload': payload
        };
        websocket.send(JSON.stringify(json));
        var event = new Event('settingsUpdated');
        document.dispatchEvent(event);
    }
}

// our method to pass values to the plugin
function sendValueToPlugin(value, param) {
    if (websocket && (websocket.readyState === 1)) {
        const json = {
            'action': actionInfo['action'],
            'event': 'sendToPlugin',
            'context': uuid,
            'payload': {
                [param]: value
            }
        };
        websocket.send(JSON.stringify(json));
    }
}

function openWebsite() {
    if (websocket && (websocket.readyState === 1)) {
        const json = {
            'event': 'openUrl',
            'payload': {
                'url': 'https://mhwlng.github.io'
            }
        };
        websocket.send(JSON.stringify(json));
    }
}

if (!isQT) {
    document.addEventListener('DOMContentLoaded', function () {
        initPropertyInspector();
    });
}

window.addEventListener('beforeunload', function (e) {
    e.preventDefault();

    // Notify the plugin we are about to leave
    sendValueToPlugin('propertyInspectorWillDisappear', 'property_inspector');

    // Don't set a returnValue to the event, otherwise Chromium with throw an error.
});

function initPropertyInspector() {
    // Place to add functions
}


function addDynamicStyles(clrs) {
    const node = document.getElementById('#sdpi-dynamic-styles') || document.createElement('style');
    if (!clrs.mouseDownColor) clrs.mouseDownColor = fadeColor(clrs.highlightColor, -100);
    const clr = clrs.highlightColor.slice(0, 7);
    const clr1 = fadeColor(clr, 100);
    const clr2 = fadeColor(clr, 60);
    const metersActiveColor = fadeColor(clr, -60);

    node.setAttribute('id', 'sdpi-dynamic-styles');
    node.innerHTML = `

    input[type="radio"]:checked + label span,
    input[type="checkbox"]:checked + label span {
        background-color: ${clrs.highlightColor};
    }

    input[type="radio"]:active:checked + label span,
    input[type="radio"]:active + label span,
    input[type="checkbox"]:active:checked + label span,
    input[type="checkbox"]:active + label span {
      background-color: ${clrs.mouseDownColor};
    }

    input[type="radio"]:active + label span,
    input[type="checkbox"]:active + label span {
      background-color: ${clrs.buttonPressedBorderColor};
    }

    td.selected,
    td.selected:hover,
    li.selected:hover,
    li.selected {
      color: white;
      background-color: ${clrs.highlightColor};
    }

    .sdpi-file-label > label:active,
    .sdpi-file-label.file:active,
    label.sdpi-file-label:active,
    label.sdpi-file-info:active,
    input[type="file"]::-webkit-file-upload-button:active,
    button:active {
      background-color: ${clrs.buttonPressedBackgroundColor};
      color: ${clrs.buttonPressedTextColor};
      border-color: ${clrs.buttonPressedBorderColor};
    }

    ::-webkit-progress-value,
    meter::-webkit-meter-optimum-value {
        background: linear-gradient(${clr2}, ${clr1} 20%, ${clr} 45%, ${clr} 55%, ${clr2})
    }

    ::-webkit-progress-value:active,
    meter::-webkit-meter-optimum-value:active {
        background: linear-gradient(${clr}, ${clr2} 20%, ${metersActiveColor} 45%, ${metersActiveColor} 55%, ${clr})
    }
    `;
    document.body.appendChild(node);
};

/** UTILITIES */

/*
    Quick utility to lighten or darken a color (doesn't take color-drifting, etc. into account)
    Usage:
    fadeColor('#061261', 100); // will lighten the color
    fadeColor('#200867'), -100); // will darken the color
*/
function fadeColor(col, amt) {
    const min = Math.min, max = Math.max;
    const num = parseInt(col.replace(/#/g, ''), 16);
    const r = min(255, max((num >> 16) + amt, 0));
    const g = min(255, max((num & 0x0000FF) + amt, 0));
    const b = min(255, max(((num >> 8) & 0x00FF) + amt, 0));
    return '#' + (g | (b << 8) | (r << 16)).toString(16).padStart(6, 0);
}

/** STREAM DOCK FILE PICKER
 *
 * Stream Dock (Ajazz / Mirabox) does not let the browser handle <input type="file">. It opens its
 * own native dialog and leaves input.value as "C:\fakepath\<name>" - the bare filename, with no
 * directory - so the path we save is useless and every image and sound setting silently does
 * nothing. The real absolute path is delivered instead by calling this global, with a JSON array
 * of paths as a string, e.g. ["M:/images/on.png"].
 *
 * Elgato's Stream Deck never calls it, so this is inert there and the normal value path still runs.
 */
var sdFilePickerId = '';

document.addEventListener('click', function (event) {
    var elem = event.target;
    if (elem && elem.tagName === 'INPUT' && elem.type === 'file') {
        sdFilePickerId = elem.id;
    }
}, true);

function onFilePickerReturn(url) {
    if (!sdFilePickerId) {
        return;
    }

    var paths = url;
    try {
        paths = JSON.parse(url);
    }
    catch (err) {
        // Already a plain string - use it as-is.
    }

    var path = Array.isArray(paths) ? paths[0] : paths;
    if (!path) {
        return;
    }

    // setSettings() reads elem.value first and falls back to this label, and on Stream Dock the
    // value is the useless fakepath name, so put the real path where the fallback will find it.
    var elemFile = document.getElementById(sdFilePickerId + 'Filename');
    if (elemFile) {
        elemFile.innerText = path;
    }

    var elem = document.getElementById(sdFilePickerId);
    if (elem) {
        elem.value = null;
    }

    console.log('onFilePickerReturn: ' + sdFilePickerId + ' <= ' + path);

    setSettings();
}
