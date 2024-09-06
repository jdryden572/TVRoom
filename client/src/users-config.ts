import UsersConfig from "./lib/UsersConfig.svelte";

const target = document.getElementById('users-config') as Element;
export default new UsersConfig({ target });