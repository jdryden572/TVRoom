<script lang="ts">
    import { type ISubscription } from "@microsoft/signalr";
    import type { BroadcastInfo, ControlPanelClient } from "./controlPanelClient";
    import { tick } from "svelte";

    export let client: ControlPanelClient;
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
    <h3>Debug info</h3>
    {#if $currentBroadcast}
        <div>
            <h5>Session ID</h5>
            <p>{$currentBroadcast?.sessionId}</p>
        </div>
        <div>
            <h5>Transcode command</h5>
            <p>ffmpeg {$currentBroadcast?.fFmpegArguments}</p>
        </div>
    {/if}
    <div class="debug-output">
        <div class="line-config">
            Retain 
            <input type="number" bind:value={messageCount}>
            lines
        </div>
        <ul>
            {#each debugMessages as msg}
                <li>{msg}</li>
            {/each}
        </ul>
    </div>
</div>

<style>
    .debug-view {
        display: flex;
        flex-direction: column;
        gap: 2em;
        padding-top: 1em;
        padding-inline: 1em;
    }

    * {
        margin: 0;
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
        display: flex;
        flex-direction: column;
        gap: 0.5em;
    }

    .line-config {
        align-self: flex-end;
    }

    .debug-output ul {
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