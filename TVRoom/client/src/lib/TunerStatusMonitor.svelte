<script lang="ts">
    import { onMount } from "svelte";
    import type { ControlPanelClient, TunerStatus } from "./controlPanelClient";
    import TunerStatusView from "./TunerStatusView.svelte";

    export let client: ControlPanelClient;
    
    onMount(() => {
        const subscription = client.subscribeToTunerStatuses(onNewTunerStatus);
        return () => subscription.dispose();
    });

    let tunerStatusArrays: TunerStatus[][] = [];
    function onNewTunerStatus(statuses: TunerStatus[]) {
        statuses.forEach((t, i) => {
            tunerStatusArrays[i] = tunerStatusArrays[i] || [];
            tunerStatusArrays[i].push(t)
        });
        tunerStatusArrays.forEach(list => {
            if (list.length > 61) list.shift();
        });
        tunerStatusArrays = tunerStatusArrays;
    }
</script>

<div class="container">
    {#each tunerStatusArrays as statuses}
        <TunerStatusView {statuses} />
    {/each}
</div>