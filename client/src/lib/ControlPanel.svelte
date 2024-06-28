<script lang="ts">
    import 'video.js/dist/video-js.css';
    import { tick } from 'svelte';
    import videojs from 'video.js';
    import type Player from 'video.js/dist/types/player';
    import { ControlPanelClient, type BroadcastInfo } from './controlPanelClient';
    import ChannelSelector from './ChannelSelector.svelte';
    import TextArea from './TextArea.svelte';
    import LiveNow from './LiveNow.svelte';
    import DebugView from './DebugView.svelte';
    import Loading from './Loading.svelte';

    const client = new ControlPanelClient();
    const { currentBroadcast, broadcastReady } = client;

    client.connect();

    let selectedChannel: string | undefined;
    let bitRateKbps: number = 8000;
    let inputVideoOptions: string = 
`-qsv_device /dev/dri/renderD128 
-f mpegts -hwaccel qsv 
-hwaccel_output_format qsv 
-extra_hw_frames 40`;

    let outputVideoOptions: string = 
`-ss 3 
-c:v h264_qsv -profile:v baseline -preset slow 
-vf "deinterlace_qsv" -g 30 
-b:v 4M -maxrate 8M 
-bufsize 16M -rc_init_occupancy 8M 
-extbrc 1 -look_ahead_depth 40 
-b_strategy 1 -bf 7 -refs 5 
-adaptive_i 1 -adaptive_b 1 
-strict -1 -bitrate_limit 0 
-async_depth 1`;

    let player: Player;

    async function startBroadcast() {
        const transcodeOptons = {
            bitRateKbps,
            inputVideoOptions: inputVideoOptions.replaceAll('\n', ' '),
            outputVideoOptions: outputVideoOptions.replaceAll('\n', ' '),
        };

        client.startBroadcast(selectedChannel ?? '', transcodeOptons);
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

    async function showVideo(sessionId: string) {
        await tick();
        console.log(document.getElementById('video'));
        player = videojs('video', {
            controls: true,
            fluid: true,
            restoreEl: true,
            sources: [{ 
                src: `streams/${sessionId}/live.m3u8`, 
                type: 'application/x-mpegURL'
            }]
        });
    }

</script>

<div class="main">
    
    {#if !$currentBroadcast}
        <div class="configuration">
            <ChannelSelector bind:selected={selectedChannel} />
            <!-- svelte-ignore a11y-label-has-associated-control -->
            <label>
                Input params
                <TextArea bind:value={inputVideoOptions} />
            </label>
            <!-- svelte-ignore a11y-label-has-associated-control -->
            <label>
                Output params
                <TextArea bind:value={outputVideoOptions} />
            </label>
            <button class="start-broadcast" on:click={startBroadcast}>
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
                    <path d="M9.6,8l6.9,4l-6.9,4Z"></path>
                    <circle cx="12" cy="12" r="8" stroke-width="1.5" fill="none" />
                </svg>
                <span>Start broadcast</span>
            </button>
        </div>
    {:else}
        <div class="active-broadcast">
            <div class="video-container">
                {#if !player || player.isDisposed()} 
                    <div class="loading">
                        <Loading />
                    </div>
                {/if}
                <!-- svelte-ignore a11y-media-has-caption -->
                <video id="video" class="video-js"></video>
            </div>
            <div class="now-playing">
                <LiveNow currentBroadcast={$currentBroadcast} />
                <button class="stop-broadcast" on:click={() => client.stopBroadcast()}>
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
                        <rect x="9" y="9" width="6" height="6" />
                        <circle cx="12" cy="12" r="8" stroke-width="1.5" fill="none" />
                    </svg>
                    <span>Stop broadcast</span>
                </button>
            </div>
        </div>
    {/if}
</div>

<!-- svelte-ignore a11y-media-has-caption -->
<!-- <video class="video-js" bind:this={videoElement} width="400" height="300"></video> -->

<hr/>
<DebugView {client} />

<style>
    button {
        font-size: 16px;
        border: none;
        border-radius: 0.25em;
        cursor: pointer;
        height: 2.5em;
        padding-inline: 1em;
    }

    button:hover {
        filter: brightness(1.1);
    }

    button svg {
        height: 2em;
        stroke: currentColor;
        fill: currentColor;
    }
    
    .start-broadcast {
        color: white;
        background-color: green;
        padding-inline: 0.5em 1em;
        display: flex;
        align-items: center;
        gap: 0.5em;
        justify-self: start;
    }

    .active-broadcast {
        display: grid;
        grid-template-columns: auto minmax(100px, 600px);
        gap: 1em;
        padding: 1em;
    }

    .now-playing {
        grid-column: 1;
        grid-row: 1;
    }
    
    .loading {
        position: absolute;
        inset: 0;
        z-index: 1;
        display: grid;
        place-items: center;
    }

    @media (max-width: 600px) {
        .active-broadcast {
            display: flex;
            flex-direction: column;
            padding: 0;
        }

        .now-playing {
            padding: 1em;
        }
    }

    .video-container {
        grid-column: 2;
        grid-row: 1;
        position: relative;
        background-color: black;
        aspect-ratio: 16/9;
    }

    .stop-broadcast {
        color: white;
        background-color: rgb(136, 0, 0);
        padding-inline: 0.5em 1em;
        display: flex;
        align-items: center;
        gap: 0.5em;
        margin-top: 1em;
    }
    
    .configuration {
        padding: 1em;
        display: grid;
        gap: 2em;
    }

    .configuration label {
        width: 100%;
        display: flex;
        flex-direction: column;
        gap: 0.5em;
    }
</style>