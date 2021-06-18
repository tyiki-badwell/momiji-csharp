"use strict";

(() => {

    const navigationStart = window.performance.timing.navigationStart;

    var ws; 

    const buf = new ArrayBuffer(Float64Array.BYTES_PER_ELEMENT + Uint8Array.BYTES_PER_ELEMENT * 4);
    const view = new DataView(buf);

    //websocket binaly send
    async function send(t, m1, m2, m3) {
        if (!ws) {
            console.log("disconnected.");
            return;
        }
        view.setFloat64(0, t, true);
        view.setUint8(8, Number(m1), true);
        view.setUint8(9, Number(m2), true);
        view.setUint8(10, Number(m3), true);
        view.setUint8(11, 0, true);
        await ws.send(buf);
    }

    async function sendCommand(command) {
        if (!ws) {
            console.log("disconnected.");
            return;
        }

        var json = { 'type': command };

        if (command === 'write') {
            json.param = {};
            document.querySelectorAll('input.param').forEach((i) => {
                if (i.type === 'number') {
                    json.param[i.dataset.name] = new Number(i.value);
                } else if (i.type === 'checkbox') {
                    json.param[i.dataset.name] = i.checked;
                } else {
                    json.param[i.dataset.name] = i.value;
                }
            });
        }

        await ws.send(JSON.stringify(json));
    }

    async function setupPeer(offerSdp) {
        var peer = new RTCPeerConnection(null);

        peer.addEventListener('connectionstatechange', async (e) => {
            console.log(e);
        });
        peer.addEventListener('icecandidate', async (e) => {
            console.log(e);
        });
        peer.addEventListener('iceconnectionstatechange', async (e) => {
            console.log(e);
        });
        peer.addEventListener('icegatheringstatechange', async (e) => {
            console.log(e);
        });
        peer.addEventListener('negotiationneeded', async (e) => {
            console.log(e);
        });
        peer.addEventListener('signalingstatechange', async (e) => {
            console.log(e);
        });

        peer.addEventListener('track', async (e) => {
            console.log(e);

            var stream = e.streams[0];
            stream.addEventListener('addtrack', async (e) => {
                console.log(e);
            });
            stream.addEventListener('removetrack', async (e) => {
                console.log(e);
            });

            const video = document.getElementById('video-area');
            video.srcObject = stream;
            video.play();
        });

        peer.setRemoteDescription(new RTCSessionDescription(offerSdp)).then(async (e) => {
            console.log(e);
            peer.createAnswer().then(async (answer) => {
                console.log(answer);
                peer.setLocalDescription(answer);
                ws.send(JSON.stringify(answer));
            }).catch(async (e) => {
                console.log(e);
            });
        }).catch(async (e) => {
            console.log(e);
        });

        return peer;
    }

    async function setupWs() {

        if (ws) {
            console.log("already connected.");
            return;
        }

        var peer;

        ws = new WebSocket(((document.location.protocol === 'https:') ? 'wss://' : 'ws://') + document.location.host + '/ws');
        ws.addEventListener('open', async (e) => {
            var input = document.getElementById('wsstatus');
            input.value = 'open';

            console.log(e);

            //こちらからは繋ぎに行かない
            /*
            peer.createOffer().then(async (offer) => {
                console.log(offer);
                return peer.setLocalDescription(offer);
            }).then(async () => {
                console.log("offer end");
                ws.send(JSON.stringify(peer.localDescription));
            }).catch(async (e) => {
                console.log(e);
            });
            */
        });
        ws.addEventListener('close', async (e) => {
            var input = document.getElementById('wsstatus');
            input.value = 'close';

            console.log(e);
            ws = null;

            if (peer) {
                peer.close();
                peer = null;
            }
        });

        ws.addEventListener('message', async (e) => {
            console.log(e);

            let param = JSON.parse(e.data);

            if (param.type === 'offer') {
                peer = await setupPeer(param);

            } else if (param.type === 'ice') {
                //
            } else if (param.type === 'param') {
                Object.entries(param.param).forEach(([key, value]) => {
                    var input = document.querySelectorAll('input.param[data-name="' + key + '"]')[0];
                    
                    if (input.type === 'checkbox') {
                        input.checked = value;
                    } else {
                        input.value = value;
                    }
                });

                /*
                document.getElementById('buffercount').value = json.BufferCount;
                document.getElementById('local').value = json.Local;
                document.getElementById('connect').value = json.Connect;
                document.getElementById('width').value = json.Width;
                document.getElementById('height').value = json.Height;
                document.getElementById('targeetbitrate').value = json.TargetBitrate;
                document.getElementById('maxframerate').value = json.MaxFrameRate;
                document.getElementById('intragrameintervalus').value = json.IntraFrameIntervalUs;
                document.getElementById('effectname').value = json.EffectName;
                document.getElementById('samplingrate').value = json.SamplingRate;
                document.getElementById('samplelength').value = json.SampleLength;
                */
            } else if (param.type === 'status') {
                var input = document.getElementById('hoststatus');
                input.value = param.value;

                if (param.value === 'run') {

                } else if (param.value === 'stop') {

                }
            }
        });

    } 

    window.onload = function () {

        document.querySelectorAll('input.rtc').forEach((i) => {
            i.addEventListener('click', async (e) => {

                const localVideo = document.getElementById('video-area-local');
                if (localVideo) {
                    const mediaConstraints = {
                        'video': false,
                        'audio': true
                    }

                    try {
                        const localStream = await navigator.mediaDevices.getUserMedia(mediaConstraints);
                        localVideo.srcObject = localStream;

                    } catch (e) {
                        console.log(e);
                    }
                }

            });
        });

        // Runner control
        document.querySelectorAll('input.control').forEach((i) => {
            i.addEventListener('click', async (e) => {
                await sendCommand(e.currentTarget.dataset.control);
            });
        });

        //websocket start
        document.querySelectorAll('input.ws').forEach((i) => {
            i.addEventListener('click', async (e) => {
                await setupWs();
            });
        });

        //non midi control
        document.querySelectorAll('input.key').forEach((i) => {
            i.addEventListener('click', async (e) => {
                var d = e.currentTarget.dataset;
                send(
                    navigationStart + window.performance.now(),
                    Number(d.shortMessage1),
                    Number(d.shortMessage2),
                    Number(d.shortMessage3)
                );
            });
        });

        //midi control
        if (navigator.requestMIDIAccess) {
            const midioutSelect = document.querySelector('#midiout');
            {
                const option = document.createElement("option");
                option.text = "(none)";
                midioutSelect.appendChild(option);
            }

            navigator.requestMIDIAccess().then((midi) => {
                midi.inputs.forEach((input) => {
                    const option = document.createElement("option");
                    option.text = input.name;
                    option.value = input.id;
                    midioutSelect.appendChild(option);
                });
            });

            midioutSelect.addEventListener("change", (event) => {
                navigator.requestMIDIAccess().then((midi) => {
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
