"use strict";

var pool = [];

window.setInterval(() => {
    if (pool.length > 0) {
        var temp = pool;
        pool = [];

        fetch('Operate/Note', {
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

window.onload = function () {
    document.querySelectorAll('input').forEach(function (i) {
        i.onclick = function () {
            pool.push(
                {
                    "receivedTime": window.performance.timing.navigationStart + window.performance.now(),
                    "data": [
                        Number(this.dataset.shortMessage1),
                        Number(this.dataset.shortMessage2),
                        Number(this.dataset.shortMessage3),
                        Number(0)
                    ]
                }
            );
        }
    });

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
                        pool.push({
                            "receivedTime": window.performance.timing.navigationStart + (short.receivedTime || short.timeStamp),
                            "data": Array.from(short.data)
                        });
                    }
                } else {
                    input.onmidimessage = undefined;
                    input.close();
                }
            });
        });
    });
};
