
window.onload = function () {
    "use strict";

    window.mixertest = {};
    window.mixertest.pool = [];

    /*
    window.setInterval(() => {
        if (pool.length > 0) {
            var temp = pool;
            pool = [];

            fetch('/Operate/Note', {
                method: 'POST',
                headers: {
                    "Content-Type": "application/json; charset=utf-8"
                },
                body: JSON.stringify(temp),
                mode: "same-origin",
                credentials: "same-origin",
                redirect: "error",
                referrer: "client"
            });
            console.log(temp);
        }
    }, 1);
    */

    var navigationStart = window.performance.timing.navigationStart;

    document.querySelectorAll('input').forEach((i) => {
        i.onclick = function () {
            var buf = new ArrayBuffer(Float64Array.BYTES_PER_ELEMENT + Uint8Array.BYTES_PER_ELEMENT * 4);
            var view = new DataView(buf);
            view.setFloat64(0, window.performance.timing.navigationStart + window.performance.now(), true);
            view.setUint8(8, Number(this.dataset.shortMessage1), true);
            view.setUint8(9, Number(this.dataset.shortMessage2), true);
            view.setUint8(10, Number(this.dataset.shortMessage3), true);
            view.setUint8(11, 0, true);
            window.mixertest.ws.send(buf);
            /*
            window.mixertest.pool.push(
                {
                    "receivedTime": window.performance.timing.navigationStart + window.performance.now(),
                    "data": [
                        Number(this.dataset.shortMessage1),
                        Number(this.dataset.shortMessage2),
                        Number(this.dataset.shortMessage3),
                        Number(0)
                    ]
                }
            );*/
        }
    });

    if (navigator.requestMIDIAccess) {
        var midioutSelect = document.querySelector('#midiout');
        {
            var option = document.createElement("option");
            option.text = "(none)";
            midioutSelect.appendChild(option);
        }

        navigator.requestMIDIAccess({ sysex: false }).then((midi) => {
            midi.inputs.forEach((input) => {
                var option = document.createElement("option");
                option.text = input.name;
                option.value = input.id;
                midioutSelect.appendChild(option);
            });
        });

        midioutSelect.addEventListener("change", (event) => {
            navigator.requestMIDIAccess({ sysex: false }).then((midi) => {
                midi.inputs.forEach((input) => {
                    if (event.target.value === input.id) {
                        input.open();
                        input.onmidimessage = function (short) {

                            var buf = new ArrayBuffer(Float64Array.BYTES_PER_ELEMENT + Uint8Array.BYTES_PER_ELEMENT * 4);
                            var view = new DataView(buf);
                            view.setFloat64(0, navigationStart + (short.receivedTime || short.timeStamp), true);
                            if (short.data.length > 0) view.setUint8(8, short.data[0], true);
                            if (short.data.length > 1) view.setUint8(9, short.data[1], true);
                            if (short.data.length > 2) view.setUint8(10, short.data[2], true);
                            if (short.data.length > 3) view.setUint8(11, short.data[3], true);
                            window.mixertest.ws.send(buf);

                            /*
                            pool.push({
                                "receivedTime": navigationStart + (short.receivedTime || short.timeStamp),
                                "data": Array.from(short.data)
                            });*/
                        }
                    } else {
                        input.onmidimessage = undefined;
                        input.close();
                    }
                });
            });
        });
    } else {
        var midioutSelect = document.querySelector('#midiout');
        midioutSelect.remove();
    }

    var audio = document.querySelector('#audio-area');
    if (audio) {
        window.mixertest.ws = new WebSocket('ws://' + document.location.host + '/ws');
        window.mixertest.ws.addEventListener('close', function (e) {
            console.log(e);
        });
        window.mixertest.ws.addEventListener('open', function (e) {
            console.log(e);
        });
        window.mixertest.ws.addEventListener('message', function (e) {
            console.log(e.data);
        });
    }

};
