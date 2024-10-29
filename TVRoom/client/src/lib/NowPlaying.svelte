<script lang="ts">
    import { onMount } from "svelte";
    import type { BroadcastInfo, ControlPanelClient, TranscodeStats } from "./controlPanelClient";
    import LiveNow from "./LiveNow.svelte";
    import TranscodeMeter from "./TranscodeMeter.svelte";

    export let client: ControlPanelClient;
    export let currentBroadcast: BroadcastInfo;
    
    let transcodeStats: TranscodeStats | undefined;
    onMount(() => {
        const unsub = client.subscribeToTranscodeStats(s => transcodeStats = s);
        return () => unsub.dispose();
    });
</script>

<div class="container">
    <div class="row">
        <LiveNow {currentBroadcast} />
        <div class="transcode-stats">
            <div class="header">Transcode</div> 
            <TranscodeMeter {transcodeStats} />
         </div>
    </div>
    <div class="row">
        <button class="stop-broadcast" on:click={() => client.stopBroadcast()}>
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
                <rect x="9" y="9" width="6" height="6" />
                <circle cx="12" cy="12" r="8" stroke-width="1.5" fill="none" />
            </svg>
            <span>Stop broadcast</span>
        </button>
        <button class="restart-transcode" on:click={() => client.restartTranscode()}>
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
                <path stroke="none" d="M4,11V4L6.34351,6.34351A7.99832,7.99832,0,1,1,4.252,14H6.3432A6.00471,6.00471,0,1,0,7.7597,7.75977L11,11Z"/>
            </svg>
            <span>Restart stream</span>
        </button>
    </div>
    
</div>

<style>
    .container {
        height: 100%;
        display: flex;
        flex-direction: column;
        gap: 1em;
        justify-content: space-between;
    }

    .row {
        display: flex;
        align-items: flex-start;
        gap: 1em;
    }

    .transcode-stats {
        border: 1px solid #282828;
        border-radius: 0.25em;   
    }

    .header {
        padding: 0.25em 0.5em;
        background-color: #282828;
        border-bottom: 1px solid #282828;
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

    .restart-transcode {
        color: rgb(172, 172, 172);
        background-color: transparent;
        border: 1px solid;
        padding-inline: 0.5em 1em;
        display: flex;
        align-items: center;
        gap: 0.5em;
        margin-top: 1em;
        transition: color 150ms ease;
    }

    .restart-transcode:hover {
        color: white;
    }
</style>