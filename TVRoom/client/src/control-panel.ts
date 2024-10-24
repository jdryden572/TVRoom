import ControlPanel from "./lib/ControlPanel.svelte";

const target = document.getElementById('control-panel') as Element;
export default new ControlPanel({ target });