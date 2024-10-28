<script lang="ts">
    import { type ISubscription } from "@microsoft/signalr";
    import type { BroadcastInfo, ControlPanelClient } from "./controlPanelClient";
    import { tick } from "svelte";

    export let client: ControlPanelClient;
    export let visible: boolean;
    
    $: currentBroadcast = client.currentBroadcast;
    $: onChange($currentBroadcast, visible);

    let debugSubscription : ISubscription<any> | undefined;
    function onChange(activeBroadcastSession: BroadcastInfo | undefined, isVisible: boolean) {
        console.log(`Broadcasting: ${!!activeBroadcastSession}. Visible: ${isVisible}`);
        if (!activeBroadcastSession) {
            debugSubscription?.dispose();
            debugSubscription = undefined;
        } else {
            if (!debugSubscription && isVisible) {
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

<style>
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