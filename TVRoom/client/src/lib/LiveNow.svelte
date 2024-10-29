<script lang="ts">
    import type { BroadcastInfo, ControlPanelClient, TranscodeStats } from "./controlPanelClient";
    import LiveNowIcon from "./LiveNowIcon.svelte";
    import LiveDuration from "./LiveDuration.svelte";

    export let currentBroadcast: BroadcastInfo | undefined;
    $: startTime = new Date(currentBroadcast?.startedAt ?? '');
</script>

<div class="live-now">
    <LiveNowIcon />
    <div>
        <h2>{currentBroadcast?.channelInfo.guideName}</h2>
        <h5>Channel {currentBroadcast?.channelInfo.guideNumber}</h5>
    </div>
    <div></div>
    <LiveDuration {startTime} />
</div>

<style>
    .live-now {
        display: grid;
        grid-template-columns: min-content 1fr;
        gap: 1em;
    }

    * {
        margin: 0;
    }
</style>