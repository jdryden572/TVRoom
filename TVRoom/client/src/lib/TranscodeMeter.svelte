<script lang="ts">
    import type { TranscodeStats } from "./controlPanelClient";

    export let transcodeStats: TranscodeStats | undefined;

    $: stats = transcodeStats ?? { speed: 0, quality: 0, framesPerSecond: 0, duplicate: 0, dropped: 0 };
    $: rotation = (stats.speed - 1.0) * 45;
</script>

<div class="container">
    <div class="arc"></div>
    <div class="arrow-box" style="--rotation: {rotation}deg"><div class="arrow"></div></div>
    <span class="scale zero-x">0x</span>
    <span class="scale one-x">1x</span>
    <span class="scale two-x">2x</span>
    <div class="speed">Speed: {stats.speed.toFixed(2)}x</div>
    <div class="fps">{stats.framesPerSecond.toFixed(0)} fps</div>
    <div class="quality">q: {stats.quality.toFixed(0)}</div>
</div>
{#if stats.duplicate || stats.dropped}
    <div class="errors">
        Dups: {stats.duplicate} &nbsp;&nbsp; Dropped: {stats.dropped}
    </div>
{/if}

<style>
    .container {
        --dial-color: #5a616f;
        position: relative;
        height: 100px;
        width: 190px;
        overflow: hidden;
        font-size: 13px;
    }

    .scale {
        position: absolute;
        font-size: 12px;
        color: var(--dial-color)
    }

    .zero-x {
        top: 36px;
        left: 9px;
    }

    .one-x {
        top: 5px;
        left: 87px;
    }

    .two-x {
        top: 36px;
        right: 10px;
    }

    .speed, .quality, .fps {
        position: absolute;
        inset: 0 40px 0 40px;
    }

    .speed {
        top: 48px;
        text-align: center;
    }

    .quality, .fps {
        top: 70px;
    }

    .quality {
        text-align: right;
    }

    .arc {
        position: absolute;
        inset: 20px 0 0 -10px;
        width: 200px;
        height: 200px;
        border: 5px solid transparent;
        border-radius: 50%;
        border-color: var(--dial-color) transparent transparent transparent;
        /* transform: rotate(45deg); */
    }

    .arrow-box {
        --rotation: 0deg;
        position: absolute;
        inset: 20px 0 20px 0;
        /* background-color: green; */
        height: 200px;
        width: 10px;
        margin-inline: auto;
        transition: transform 100ms ease;
        transform: rotate(var(--rotation));
    }

    .arrow {
        border: 5px solid white;
        border-color: transparent transparent white transparent;
        transform: scaleY(2);
    }

    .errors {
        font-size: 13px;
        text-align: center;
        margin: 5px 10px;
    }
</style>