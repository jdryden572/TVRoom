<script lang="ts">
    import 'video.js/dist/video-js.css';
    import { HubConnectionBuilder, LogLevel, type ISubscription } from '@microsoft/signalr';
    import { tick } from 'svelte';
    import videojs from 'video.js';
    import type Player from 'video.js/dist/types/player';
    import { ControlPanelClient, type BroadcastInfo, type TranscodeOptions } from './controlPanelClient';
    import ChannelSelector from './ChannelSelector.svelte';

    const client = new ControlPanelClient();
    const { currentBroadcast, broadcastReady } = client;

    client.connect();

    let selectedChannel: string | undefined;
    let bitRateKbps: number = 8000;
    let inputVideoOptions: string = '-vaapi_device /dev/dri/renderD128 -f mpegts -hwaccel vaapi -hwaccel_output_format vaapi';
    let outputVideoOptions: string = '-ss 3 -c:v h264_vaapi -vf "format=nv12|vaapi,deinterlace_vaapi" -g 30';
    let debugMessages: string[] = [];
    let videoElement: HTMLElement;
    let player: Player;

    async function startBroadcast() {
        const transcodeOptons = {
            bitRateKbps,
            inputVideoOptions,
            outputVideoOptions,
        };

        client.startBroadcast(selectedChannel ?? '', transcodeOptons);
    }

    $: onBroadcastChange($currentBroadcast);

    let debugSubscription : ISubscription<any> | undefined;
    function onBroadcastChange(activeBroadcastSession: BroadcastInfo | undefined) {
        if (!activeBroadcastSession) {
            console.log('broadcast stopped');
            debugSubscription?.dispose();
            debugSubscription = undefined;
        } else {
            if (!debugSubscription) {
                debugSubscription = client.subscribeToDebugOutput(addDebugMessage);
                console.log('broadcast started: ' + activeBroadcastSession.sessionId);
            }
        }
    }

    $: onBroadcastReady($broadcastReady);
    function onBroadcastReady(activeBroadcastSession: BroadcastInfo | undefined) {
        if (activeBroadcastSession) {
            console.log('ready');
            showVideo(activeBroadcastSession.sessionId);
        } else {
            console.log('not ready');
            player?.dispose();
        }
    }

    function showVideo(sessionId: string) {
        player = videojs(videoElement, {
            controls: true,
            restoreEl: true,
            sources: [{ 
                src: `streams/${sessionId}/live.m3u8`, 
                type: 'application/x-mpegURL'
            }]
        });
    }

    let messagesElement: HTMLElement;
    async function addDebugMessage(msg: string) {
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
    
<pre>{JSON.stringify($currentBroadcast, null, 2)}</pre>
<ChannelSelector bind:selected={selectedChannel} />

<button on:click={startBroadcast}>Start broadcast</button>
<button on:click={() => client.stopBroadcast()}>Stop broadcast</button>
<div class="transcode-options">
    <input bind:value={bitRateKbps} type="number" />
    <input bind:value={inputVideoOptions} type="text" />
    <input bind:value={outputVideoOptions} type="text" />
</div>

<ul class="debug-output" bind:this={messagesElement}>
    {#each debugMessages as msg}
        <li>{msg}</li>
    {/each}
</ul>

<style>
    .transcode-options {
        margin: 1em;
    }
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