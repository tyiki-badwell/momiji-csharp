"use strict";

(() => {

    const navigationStart = window.performance.timing.navigationStart;

    var ws; 
    const buf = new ArrayBuffer(Float64Array.BYTES_PER_ELEMENT + Uint8Array.BYTES_PER_ELEMENT * 4);
    const view = new DataView(buf);

    //websocket binaly send
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

    function sendCommand(command) {
        if (!ws) {
            console.log("disconnected.");
            return;
        }
        ws.send(JSON.stringify({ 'type': command }));
    }

    async function setup() {

        const video = document.getElementById('video-area');

        const peer = new RTCPeerConnection(null);

        peer.addEventListener('track', async (e) => {
            console.log(e);
            video.srcObject = e.streams[0];
            video.play();
        });
        peer.addEventListener('removetrack', async (e) => {
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
        peer.addEventListener('signalingstatechange', async (e) => {
            console.log(e);
        });
        peer.addEventListener('negotiationneeded', async (e) => {
            console.log(e);
        });

        peer.createOffer().then(async (offer) => {
            console.log(offer);
            return peer.setLocalDescription(offer);
        }).then(async () => {
            console.log("offer end");
            ws.send(JSON.stringify(peer.localDescription));
        }).catch(async (e) => {
            console.log(e);
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
                sendCommand(e.currentTarget.dataset.control);
            });
        });

        //websocket start
        document.querySelectorAll('input.ws').forEach((i) => {
            i.addEventListener('click', async (e) => {
                if (ws) {
                    console.log("already connected.");
                    return;
                } 

                ws = new WebSocket(((document.location.protocol === 'https:') ? 'wss://' : 'ws://') + document.location.host + '/ws');
                ws.addEventListener('open', async (e) => {
                    console.log(e);
                    await setup();
                });
                ws.addEventListener('close', async (e) => {
                    console.log(e);
                    ws = null;
                });
                ws.addEventListener('message', async (e) => {
                    console.log(e);
                });
            });
        });

        //non midi control
        document.querySelectorAll('input.key').forEach((i) => {
            i.addEventListener('click', async (e) => {
                send(
                    navigationStart + window.performance.now(),
                    Number(this.dataset.shortMessage1),
                    Number(this.dataset.shortMessage2),
                    Number(this.dataset.shortMessage3)
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
