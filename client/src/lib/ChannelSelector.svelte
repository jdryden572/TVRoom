<script lang="ts">
    export let selected: string | undefined;

    const channelListPromise = loadChannelList();

    interface Channel {
        guideNumber: string,
        guideName: string,
    }

    async function loadChannelList() {
        const resp = await fetch('/channels');
        return await resp.json() as Channel[];
    }
</script>

<select bind:value={selected}>
    {#await channelListPromise}
        <option>Select a channel</option>
    {:then channelList} 
        {#each channelList as channel}
            <option value={channel.guideNumber}>{channel.guideNumber} - {channel.guideName}</option>
        {/each}
    {/await}
</select>