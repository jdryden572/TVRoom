<script lang="ts">
    import TextArea from "./TextArea.svelte";

    let name: string;
    let inputVideoParameters: string;
    let outputVideoParameters: string;

    let latest: string;
    $: unsaved = latest !== JSON.stringify({ name, inputVideoParameters, outputVideoParameters });

    interface TranscodeConfig {
        name: string,
        inputVideoParameters: string,
        outputVideoParameters: string,
    }

    load();

    async function load() {
        const resp = await fetch('/transcodeConfig');
        const config = await resp.json() as TranscodeConfig;
        ({ name, inputVideoParameters, outputVideoParameters } = config);
        setLatest(config);
    }

    async function saveConfig() {
        const config: TranscodeConfig = {
            name,
            inputVideoParameters: inputVideoParameters.replaceAll(/\r|\n/g, ' '),
            outputVideoParameters: outputVideoParameters.replaceAll(/\r|\n/g, ' '),
        };

        await fetch('/transcodeConfig', {
            method: 'POST',
            body: JSON.stringify(config),
            headers: { 'Content-Type': 'application/json' },
        });

        setLatest(config);
    }

    function setLatest({ name, inputVideoParameters, outputVideoParameters } : TranscodeConfig) {
        latest = JSON.stringify({ name, inputVideoParameters, outputVideoParameters });
    }
</script>

<details class="transcode-config-section">
    <summary>
        Transcode config
        {#if name}
            <span class="config-name" class:unsaved={unsaved}>{name ?? ''}</span>
        {/if}
    </summary>
    <div class="main">
        <label>
            Name
            <input type="text" bind:value={name} />
        </label>
        <!-- svelte-ignore a11y-label-has-associated-control -->
        <label>
            Input video parameters
            <TextArea bind:value={inputVideoParameters} />
        </label>
        <!-- svelte-ignore a11y-label-has-associated-control -->
        <label>
            Output video parameters
            <TextArea bind:value={outputVideoParameters} />
        </label>
        <div class="button-bar">
            {#if unsaved} 
                <strong class="unsaved-msg">Unsaved changes</strong>
            {/if}
            <button class="save" on:click={saveConfig}>Save config</button>
        </div>
    </div>
</details>

<style>
    details {
        --unsaved-color: rgb(192, 6, 6);
        display: flex;
        flex-direction: column;
        border: 1px solid #282828;
    }

    summary {
        cursor: pointer;
        padding: 1em;
        align-self: flex-start;
    }

    summary:hover {
        background-color: rgb(28, 33, 44);
    }

    .main {
        display: flex;
        flex-direction: column;
        gap: 1em;
        padding: 1em
    }

    .config-name {
        background-color: rgb(28, 33, 44);
        padding: 0.5em 0.75em;
        border-radius: 0.25em;
        margin-left: 0.5em;
    }

    .config-name.unsaved {
        border: 2px solid var(--unsaved-color);
    }

    label {
        user-select: none;
        display: flex;
        flex-direction: column;
        gap: 0.5em;
    }

    input {
        font-size: 16px;
        font-family: consolas, monospace;
        color: rgb(168, 168, 168);
        background-color: rgb(28, 33, 44);
        border: none;
        padding: 0.5em;
    }

    .button-bar {
        display: flex;
        align-items: center;
    }

    .unsaved-msg {
        color: var(--unsaved-color);
    }

    .save {
        margin-left: auto;
        background-color: #25258d;
        color: rgb(216, 216, 216);
    }
</style>