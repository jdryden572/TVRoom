<script lang="ts">
    import type { TunerStatus } from "./controlPanelClient";

    export let tuner: TunerStatus;
</script>

<div class="tuner">
    <div class="header">
        <span>{tuner.resource}</span>
    </div>
    {#if tuner.channelNumber}
        <div class="active">
            <div class="info">
                <h2>{tuner.channelName}</h2>
                <h5>Channel {tuner.channelNumber}</h5>
            </div>
            <div class="strengths">
                <div class="bar">
                    <label>
                        Signal strength
                        <progress max="100" value={tuner.signalStrengthPercent}></progress>
                    </label>
                    <span>{tuner.signalStrengthPercent}</span>
                </div>
                <div class="bar">
                    <label>
                        Signal quality
                        <progress max="100" value={tuner.signalQualityPercent}></progress>
                    </label>
                    <span>{tuner.signalQualityPercent}</span>
                </div>
                <div class="bar">
                    <label>
                        Symbol quality
                        <progress max="100" value={tuner.symbolQualityPercent}></progress>
                    </label>
                    <span>{tuner.symbolQualityPercent}</span>
                </div>
            </div>
        </div>
    {:else}
        <div class="inactive">
            Tuner inactive
        </div>
    {/if}
</div>

<style>
    .tuner {
        margin: 1em;
        
        border: 1px solid #282828;
        border-radius: 0.25em;   
    }

    .header {
        padding: 0.25em 0.5em;
        background-color: #282828;
        border-bottom: 1px solid #282828;
    }

    .inactive {
        font-style: italic;
        color: #818181;
        padding: 1em;
    }

    .active {
        display: flex;
        align-items: center;
        gap: 1em;
        padding: 0.5em;
    }

    .info * {
        margin: 0;
    }
    
    .bar {
        display: flex;
        align-items: flex-end;
        gap: 0.5em;
    }
    .bar label {
        display: flex;
        flex-direction: column;
    }
</style>