<script>
    import 'video.js/dist/video-js.css';
    import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
    import { tick } from 'svelte';
    import videojs from 'video.js';

    let debugMessages = [];
    let activeSession;
    let videoElement;
    let player;

    const connection = new HubConnectionBuilder()
        .withUrl("/controlPanelHub")
        .configureLogging(LogLevel.Debug)
        .build();

    connection.on('BroadcastStarted', session => {
        activeSession = session;
        showVideo();
        subscribeToDebugOutput();
    });
    connection.on('BroadcastStopped', () => {
        activeSession = undefined;
        player?.options({sources: []});
    });

    startConnection();

    async function startConnection() {
        try {
            await connection.start();
            activeSession = await connection.invoke("GetCurrentSession");
            if (activeSession) {
                showVideo();
                subscribeToDebugOutput();
            }
        } catch (err) {
            console.error(err);
        }
    }

    async function startBroadcast() {
        const channelInfo = {
            guideNumber: 'CBS',
            guideName: 'CBS',
            url: 'http://hdhr-106903ac.local:5004/auto/v3.1'
        };
        const transcodeOptions = {
            bitRateKbps: 8000,
            outputVideoOptions: '-vf yadif -c:v libx264 -g 30'
        };

        try {
            await connection.invoke('StartBroadcast', channelInfo, transcodeOptions);
            subscribeToDebugOutput();
        }
        catch (err) {
            console.error(err);
        }
    }

    async function stopBroadcast() {
        try {
            await connection.invoke('StopBroadcast');
            activeSession = undefined;
        }
        catch (err) {
            console.error(err);
        }
    }

    async function showVideo() {
        await tick();
        player = videojs(videoElement, {
            controls: true,
            sources: [{ 
                src: `streams/${activeSession.sessionId}/live.m3u8`, 
                type: 'application/x-mpegURL' 
            }]
        });
    }

    let debugSubscription;
    function subscribeToDebugOutput() {
        if (!debugSubscription) {
            debugMessages = [];
            debugSubscription = connection.stream('GetDebugOutput')
                .subscribe({
                    next: addDebugMessage,
                    complete: () => debugSubscription = undefined,
                    error: console.error,
                });
        }
    }

    let messagesElement;
    async function addDebugMessage(msg) {
        const alreadyScrolledToBottom = messagesElement.scrollHeight - messagesElement.scrollTop - messagesElement.clientHeight < 1;
        
        debugMessages.push(msg); 
        debugMessages = debugMessages; 

        if (alreadyScrolledToBottom) {
            await tick();
            messagesElement.scrollTop = messagesElement.scrollHeight;
        }
    }
</script>

<!-- svelte-ignore a11y-media-has-caption -->
<video class="video-js" bind:this={videoElement} width="400" height="300"></video>
    
<pre>{JSON.stringify(activeSession, null, 2)}</pre>

<button on:click={startBroadcast}>Start broadcast</button>
<button on:click={stopBroadcast}>Stop broadcast</button>

<ul class="debug-output" bind:this={messagesElement}>
    {#each debugMessages as msg}
        <li>{msg}</li>
    {/each}
</ul>

<style>
    .debug-output {
        font-family: consolas;
        font-size: 12px;
        list-style-type: none;
        padding-inline-start: 0;
        height: 400px;
        overflow: auto;
    }
    .debug-output li:hover {
        background-color: rgba(255, 255, 255, 0.096);
    }
</style>