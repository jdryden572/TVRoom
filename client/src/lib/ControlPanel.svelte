<script lang="ts">
    import 'video.js/dist/video-js.css';
    import { tick } from 'svelte';
    import videojs from 'video.js';
    import type Player from 'video.js/dist/types/player';
    import { ControlPanelClient, type BroadcastInfo } from './controlPanelClient';
    import ChannelSelector from './ChannelSelector.svelte';
    import LiveNow from './LiveNow.svelte';
    import DebugView from './DebugView.svelte';
    import Loading from './Loading.svelte';
    import TranscodeConfig from './TranscodeConfig.svelte';

    const client = new ControlPanelClient();
    const { currentBroadcast, broadcastReady } = client;

    startConnection();

    async function startConnection() {
        await client.connect();
        const latestChannel = await client.getLastChannel();
        if (latestChannel) {
            selectedChannel = latestChannel.guideNumber;
        }
    }

    let selectedChannel: string | undefined;

    let player: Player;

    async function startBroadcast() {
        client.startBroadcast(selectedChannel ?? '');
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
                src: `streams/${sessionId}/master.m3u8`, 
                type: 'application/x-mpegURL'
            }]
        });
    }

</script>

<div class="links">
    <a href="/UsersConfig">Manage Users</a>
</div>

<div class="main">
    
    {#if !$currentBroadcast}
        <div class="configuration">
            <ChannelSelector bind:selected={selectedChannel} />
            <TranscodeConfig />
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

<hr/>
<DebugView {client} />

<style>
    .links {
        margin-inline: 1em;
        display: flex;
        justify-content: flex-end;
        gap: 1em
    }

    .links a {
        color: #8b8b8b;
        text-decoration: none;
        transition: color 150ms ease;
        line-height: 2em;
    }

    .links a:hover {
        color: white;
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
</style>