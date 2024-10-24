<script lang="ts">
    import { onMount } from "svelte";

	export let startTime: Date;
    $: tooltip = `Started at ${startTime.toLocaleString().replace(',', '')}`;

	onMount(() => {
        const handle = setInterval(updateDuration, 100);
        return () => clearInterval(handle);
    });

	let duration = '';
	
	function updateDuration() {
		const durationMillis = new Date().getTime() - startTime.getTime();
        duration = formatDuration(durationMillis)
	}
	
	function formatDuration(millis: number) {
		const hours = Math.floor(millis / 3600000);
		millis = millis - (hours * 3600000);
		const minutes = Math.floor(millis / 60000);
		millis = millis - (minutes * 60000);
		const seconds = Math.floor(millis / 1000);

		let time = minutes.toString().padStart(2, '0') + ':' + 
			seconds.toString().padStart(2, '0');
			
		return hours > 0 ?
			hours.toString().padStart(2, '0') + ':' + time :
			time; 
	}
</script>

<span title={tooltip}>Live for {duration}</span>

<style>
	span {
		margin-top: 0.2em;
		color: white
	}
</style>