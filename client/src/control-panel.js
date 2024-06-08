import ControlPanel from "./lib/ControlPanel.svelte";

const target = document.getElementById('control-panel');
export default new ControlPanel({ target });