"use strict";

(() => {

    const navigationStart = window.performance.timing.navigationStart;
    
    const queue = [];
    const ms = new MediaSource();
    var sb;

    const mimeCodec = 'audio/webm; codecs="opus';
    if (MediaSource.isTypeSupported(mimeCodec)) {
        ms.addEventListener('sourceopen', (e) => {
            console.log(e);
            sb = ms.addSourceBuffer(mimeCodec);
            sb.addEventListener('update', (e) => {
                console.log(e);
/*                if (queue.length > 0) {
                    e.target.appendBuffer(queue.shift());
                }*/
            });
            sb.addEventListener('abort', (e) => {
                console.log(e);
            });
            sb.addEventListener('error', (e) => {
                console.log(e);
            });
            sb.addEventListener('updatestart', (e) => {
                console.log(e);
            });
            sb.addEventListener('updateend', (e) => {
                console.log(e);
            });

            const audio = document.querySelector('#audio-area');
            if (audio) {
                audio.srcObject = ms;
                audio.play();
            }

        });
        ms.addEventListener('sourceended', (e) => {
            console.log(e);
        });
        ms.addEventListener('sourceclose', (e) => {
            console.log(e);
        });
    }
    



    /*
    var pool = [];
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

    window.onload = function () {
        var requestVerificationToken = document.getElementById('RequestVerificationToken').value;

        document.querySelectorAll('input.control').forEach((i) => {
            i.onclick = function () {
                fetch('/?handler=' + this.dataset.control, {
                    method: 'POST',
                    headers: {
                        "Content-Type": "application/json; charset=utf-8",
                        "RequestVerificationToken": requestVerificationToken
                    },
                    body: "",
                    mode: "same-origin",
                    credentials: "same-origin",
                    redirect: "error",
                    referrer: "client"
                });
            }
        });

        var ws; 
        const buf = new ArrayBuffer(Float64Array.BYTES_PER_ELEMENT + Uint8Array.BYTES_PER_ELEMENT * 4);
        const view = new DataView(buf);

        function send(t, m1, m2, m3) {
            if (!ws) {
                console.log("disconnected.");
                return;
            }
            view.setFloat64(0, t, true);
            view.setUint8(8, Number(m1), true);
            view.setUint8(9, Number(m2), true);
            view.setUint8(10, Number(m3), true);
            view.setUint8(11, 0, true);
            ws.send(buf);
        }

        document.querySelectorAll('input.ws').forEach((i) => {
            i.onclick = function () {
                if (ws) {
                    console.log("already connected.");
                    return;
                } 

                ws = new WebSocket(((document.location.protocol === 'https:') ? 'wss://' : 'ws://') + document.location.host + '/ws');
                ws.addEventListener('close', (e) => {
                    ws = null;
                    console.log(e);
                });
                ws.addEventListener('open', (e) => {
                    console.log(e);
                });
                ws.addEventListener('message', (e) => {
                    if (!sb) return;

                    if (queue.length > 100) return;

                    const fr = new FileReader();
                    fr.addEventListener('load', (e) => {
                        //queue.push(e.target.result);
                        if (sb) sb.appendBuffer(e.target.result);
                        /*
                        ac.decodeAudioData(e.target.result)
                            .then((buffer) => {
                                console.log(buffer);
                            })
                            .catch((err) => {
                                console.log(err);
                            });*/
                    });
                    fr.readAsArrayBuffer(e.data);
                });
            }
        });

        document.querySelectorAll('input.key').forEach((i) => {
            i.onclick = function () {
                send(
                    navigationStart + window.performance.now(),
                    Number(this.dataset.shortMessage1),
                    Number(this.dataset.shortMessage2),
                    Number(this.dataset.shortMessage3)
                );

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
            const midioutSelect = document.querySelector('#midiout');
            {
                const option = document.createElement("option");
                option.text = "(none)";
                midioutSelect.appendChild(option);
            }

            navigator.requestMIDIAccess({ sysex: false }).then((midi) => {
                midi.inputs.forEach((input) => {
                    const option = document.createElement("option");
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
                                send(
                                    navigationStart + (short.receivedTime || short.timeStamp),
                                    (short.data.length > 0) ? short.data[0] : 0,
                                    (short.data.length > 1) ? short.data[1] : 0,
                                    (short.data.length > 2) ? short.data[2] : 0
                                );

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
            const midioutSelect = document.querySelector('#midiout');
            midioutSelect.remove();
        }

    };

}) ();
