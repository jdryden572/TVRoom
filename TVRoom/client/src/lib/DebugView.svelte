<script lang="ts">
    import { type ISubscription } from "@microsoft/signalr";
    import type { BroadcastInfo, ControlPanelClient } from "./controlPanelClient";
    import { tick } from "svelte";
    import TunerStatusMonitor from "./TunerStatusMonitor.svelte";
    import TranscodeLogs from "./TranscodeLogs.svelte";
    import DebugTranscodeOutput from "./DebugTranscodeOutput.svelte";

    export let client: ControlPanelClient;
    $: clientConnected = client.connected;
    $: currentBroadcast = client.currentBroadcast;

    let transcodeDebugLogsElement: HTMLDetailsElement;
    let transcodeDebugLogsVisible = false;
</script>

<div class="debug-view">
    {#if $clientConnected}
        <details open>
            <summary>Tuner status</summary>
            <TunerStatusMonitor {client} />
        </details>
        
        <details bind:this={transcodeDebugLogsElement} on:toggle={e => transcodeDebugLogsVisible = transcodeDebugLogsElement.open}>
            <summary>Transcode output</summary>
            <DebugTranscodeOutput {client} visible={transcodeDebugLogsVisible} />
        </details>
    {/if}

    {#if $currentBroadcast}
        <details>
            <summary>Session info</summary>
            <div class="session-details">
                <div>
                    <h5>Session ID</h5>
                    <p>{$currentBroadcast?.sessionId}</p>
                </div>
                <div>
                    <h5>Transcode command</h5>
                    <p>ffmpeg {$currentBroadcast?.fFmpegArguments}</p>
                </div>
            </div>
        </details>
    {/if}

    <details>
        <summary>Transcode logs</summary>
        <TranscodeLogs />
    </details>
</div>

<style>
    h5 {
        margin: 0;
    }

    .debug-view {
        display: flex;
        flex-direction: column;
        gap: 2em;
        padding-top: 1em;
        padding-inline: 1em;
        margin-bottom: 2em;
    }

    .session-details {
        margin: 1em;
    }

    details {
        border: 1px solid #282828;
    }

    details h5 {
        margin: 0;
    }

    summary {
        cursor: pointer;
        padding: 0.5em 1em;
        align-self: flex-start;
    }

    summary:hover {
        background-color: rgb(28, 33, 44);
    }

    p {
        font-family: consolas, monospace;
        word-break: break-all;
    }
</style>