<script lang="ts">
    import { onMount } from "svelte";
    import type { TunerStatus } from "./controlPanelClient";
    import Chart from "chart.js/auto";
    import 'chartjs-adapter-date-fns';

    export let statuses: TunerStatus[];

    $: onNewTunerStatuses(statuses);
    function onNewTunerStatuses(newTunerStatuses: TunerStatus[]) {
        if (chart) {
            const signalStrengths: [number, number][] = [];
            const signalQualities: [number, number][] = [];
            const symbolQualities: [number, number][] = [];
            for (const newTunerStatus of newTunerStatuses) {
                const {
                    timestamp, 
                    signalStrengthPercent, 
                    signalQualityPercent, 
                    symbolQualityPercent
                } = newTunerStatus;

                signalStrengths.push([timestamp, signalStrengthPercent]);
                signalQualities.push([timestamp, signalQualityPercent]);
                symbolQualities.push([timestamp, symbolQualityPercent]);
            }

            chart.config.data.datasets[0].data = signalStrengths;
            chart.config.data.datasets[1].data = signalQualities;
            chart.config.data.datasets[2].data = symbolQualities;

            const timestamp = newTunerStatuses[newTunerStatuses.length - 1]?.timestamp;
            const timeScale = chart.config.options?.scales?.['x'];
            if (timeScale) {
                timeScale.min = timestamp - 60000;
                timeScale.max = timestamp;
            }

            chart.update();
        }
    }

    // function pushToDataSet(index: number, timestamp: number, value: number) {
    //     const data = chart.config.data.datasets[index].data;
    //     data.push([timestamp, value]);
    //     if (data.length > 60) {
    //         data.splice(0, 1);
    //     }
    // }

    let chartElement: HTMLCanvasElement;
    let chart: Chart;

    onMount(() => {
        chart = new Chart(
            chartElement,
            {
                type: 'line',
                data: {
                    datasets: [{
                        label: 'Signal strength',
                        data: [],
                        borderWidth: 2,
                        fill: false,
                        pointStyle: false,
                        borderColor: 'red',
                        tension: 0.1
                    }, {
                        label: 'Signal quality',
                        data: [],
                        borderWidth: 2,
                        fill: false,
                        pointStyle: false,
                        borderColor: 'green',
                        tension: 0.1
                    }, {
                        label: 'Symbol quality',
                        data: [],
                        borderWidth: 2,
                        fill: false,
                        pointStyle: false,
                        borderColor: '#0075ff',
                        tension: 0.1
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    animation: false,
                    scales: {
                        x: {
                            type: 'time',
                            time: {
                                unit: 'second',
                                displayFormats: {
                                    second: 'HH:mm:ss'
                                }
                            },
                            ticks: {
                                stepSize: 15,
                            }
                        },
                        y: {
                            min: 50,
                            max: 100,
                        }
                    },
                    plugins: {
                        legend: {
                            display: false,
                        }
                    }
                }
            });
    })
</script>

<div class="chart">
    <canvas bind:this={chartElement}></canvas>
</div>

<style>
    .chart {
        position: relative;
        width: 100%;
        height: 100%;
    }
</style>