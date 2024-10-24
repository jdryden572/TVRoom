<script lang="ts">
    import { type ISubscription } from "@microsoft/signalr";
    import type { BroadcastInfo, ControlPanelClient } from "./controlPanelClient";
    import { tick } from "svelte";
    import TunerStatusMonitor from "./TunerStatusMonitor.svelte";
    import TranscodeLogs from "./TranscodeLogs.svelte";

    export let client: ControlPanelClient;
    $: clientConnected = client.connected;
    $: currentBroadcast = client.currentBroadcast;

    $: onBroadcastChange($currentBroadcast);

    let debugSubscription : ISubscription<any> | undefined;
    function onBroadcastChange(activeBroadcastSession: BroadcastInfo | undefined) {
        if (!activeBroadcastSession) {
            debugSubscription?.dispose();
            debugSubscription = undefined;
        } else {
            if (!debugSubscription) {
                debugSubscription = client.subscribeToDebugOutput(addDebugMessage);
            }
        }
    }

    let messageCount = 400;
    let debugMessages: string[] = [];
    async function addDebugMessage(msg: string) {
        const messagesElement = document.querySelector('.debug-output ul');
        if (!messagesElement) {
            return;
        }

        const alreadyScrolledToBottom = messagesElement.scrollHeight - messagesElement.scrollTop - messagesElement.clientHeight < 1;
        
        debugMessages.push(msg);
        if (debugMessages.length > messageCount) {
            debugMessages.splice(0, debugMessages.length - messageCount);
        }
        debugMessages = debugMessages; 

        if (alreadyScrolledToBottom) {
            await tick();
            messagesElement.scrollTop = messagesElement.scrollHeight;
        }
    }

</script>

<div class="debug-view">
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
    <details open>
        <summary>Transcode output</summary>
        <div class="debug-output">
            <div class="line-config">
                Retain 
                <input type="number" bind:value={messageCount}>
                lines
                <button class=clear on:click={() => debugMessages.length = 0}>Clear</button>
            </div>
            <ul>
                {#each debugMessages as msg}
                    <li>{msg}</li>
                {/each}
            </ul>
        </div>
    </details>

    {#if $clientConnected}
        <details>
            <summary>Tuner status</summary>
            <TunerStatusMonitor {client} />
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

    input[type=number] {
        font-size: 14px;
        padding: 0.25em;
        color: rgb(219, 219, 219);
        background-color: rgb(28, 33, 44);
        border: 1px solid rgb(168, 168, 168);
        width: 8ch;
    }

    .debug-output {
        margin-top: 0.5em;
        display: flex;
        flex-direction: column;
        gap: 0.5em;
    }

    .line-config {
        margin-right: 0.5em;
        align-self: flex-end;
        color: rgb(219, 219, 219);
    }

    .clear {
        margin-left: 0.5em;
        padding: 0.35em 0.5em;
        height: unset;
        background: none;
        color: inherit;
        border: 1px solid currentColor;
    }

    .debug-output ul {
        margin: 0;
        font-family: consolas;
        font-size: 12px;
        list-style-type: none;
        padding-inline-start: 0;
        padding: 0.5em;
        height: 300px;
        overflow: auto;
        resize: vertical;
        border: 1px solid rgb(28, 33, 44);
    }

    .debug-output li:hover {
        background-color: rgba(255, 255, 255, 0.096);
    }
</style>