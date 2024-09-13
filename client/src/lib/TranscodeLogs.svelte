<script lang="ts">
    import { onMount } from "svelte";

    refreshLogList();

    let logs: LogFile[] = [];
    async function refreshLogList() {
        const resp = await fetch("/logs");
        const list = await resp.json();
        logs = list as LogFile[];
        logs.sort((b, a) => {
            if (a.name < b.name) return -1;
            if (a.name > b.name) return 1;
            return 0;
        });
    }

    async function deleteLogFile(name: string) {
        const resp = await fetch(`/logs/${name}`, {
            method: 'DELETE'
        });
        if (resp.ok) {
            await refreshLogList();
        }
    }

    const oneKb = 1024;
    const oneMb = oneKb * 1024;
    function formatSize(bytes: number) : string {
        if (bytes > oneMb) {
            const mbs = (bytes / oneMb).toFixed(1);
            return `${mbs} MB`;
        }

        const kbs = (bytes / oneKb).toFixed(1);
        return `${kbs} KB`;
    }

    interface LogFile {
        name: string,
        length: number,
    }
</script>

<div class="container">
    <button on:click={() => refreshLogList()}>
        <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24"><path d="M13,11l3.22-3.22A5.91,5.91,0,0,0,12,6a6,6,0,1,0,5.65,8h2.08a8,8,0,1,1-2.08-7.65L20,4v7Z"/></svg>
        Refresh
    </button>
    {#each logs as log}
        <div class="log-file">
            <span class="name">{log.name}</span>
            <a href="/logs/{log.name}" download={log.name}>
                <button class="download">
                    <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24"><path d="M15,4H9v6H5l7,7,7-7H15ZM12,14.17,9.83,12H11V6h2v6h1.17ZM5,19H19v2H5Z"/></svg>
                    <span>{formatSize(log.length)}</span>
                </button>
            </a>
            <button on:click={() => deleteLogFile(log.name)}>
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24"><path d="M9,3V4H4V6H5V19a2,2,0,0,0,2,2H17a2,2,0,0,0,2-2V6h1V4H15V3H9M7,6H17V19H7V6M9,8v9h2V8H9m4,0v9h2V8Z"/></svg>
            </button>
        </div>
    {/each}
</div>

<style>
    .container {
        padding: 0.5em 1em;
    }

    .name {
        word-wrap: break-word;
        min-width: 0;
    }

    .log-file {
        padding-block: 0.5em;
        display: flex;
        align-items: center;
        gap: 1em;
        min-width: 0;
    }

    a {
        text-decoration: none;
        color: currentColor;
        flex-shrink: 0;
    }

    .download {
        background-color: black;
        padding: 0.5em;
        display: flex;
        align-items: center;
        color: rgb(156, 156, 156);
    }

    .download svg {
        stroke-width: 0.2px;
    }

    button {
        background-color: black;
        padding: 0.5em;
        display: flex;
        gap: 0.25em;
        flex-shrink: 0;
        align-items: center;
        color: rgb(156, 156, 156);
    }

    button svg {
        stroke-width: 0.2px;
    }
</style>