<script lang="ts">
    import { onMount } from "svelte";
    import type { ControlPanelClient, TunerStatus } from "./controlPanelClient";
    import type { ISubscription } from "@microsoft/signalr";
    import TunerStatusView from "./TunerStatusView.svelte";

    export let client: ControlPanelClient;
    
    onMount(() => {
        const subscription = client.subscribeToTunerStatuses(onNewTunerStatus);
        return () => subscription.dispose();
    });

    let tunerStatuses: TunerStatus[] = [];
    function onNewTunerStatus(statuses: TunerStatus[]) {
        tunerStatuses = statuses;
    }
</script>

<div class="container">
    {#each tunerStatuses as tuner}
        <TunerStatusView {tuner} />
    {/each}
</div>