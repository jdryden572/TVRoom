<script lang="ts">
    const emailRegex = /^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$/;

    let userList: AuthorizedUser[] = [];
    let newEmail: string;
    let newRole: Role;

    loadAllUsers();

    async function loadAllUsers() {
        const resp = await fetch("/users");
        userList = await resp.json() as AuthorizedUser[];
    }

    async function addUser() {
        if (!emailRegex.test(newEmail)) {
            return;
        }
        
        const resp = await fetch('/users', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify({
                email: newEmail,
                role: newRole,
            })
        });

        if (resp.ok) {
            await loadAllUsers();
            newEmail = '';
            newRole = 'Viewer';
        }
    }

    async function removeUser(user: AuthorizedUser) {
        if (!confirm(`Remove user '${user.email}'?`)) {
            return;
        }

        const resp = await fetch(`/users/${user.id}`, {
            method: 'DELETE',
        });
        if (resp.ok) {
            await loadAllUsers();
        }
    }

    interface AuthorizedUser {
        id: number,
        email: string,
        role: Role,
    };

    type Role = 'Viewer' | 'Administrator';
</script>

<div class="users-list">
    <h1>Authorized users</h1>
    <div class="users-grid">
        {#each userList as user}
            <span>{user.email}</span>
            <span>{user.role}</span>
            <button class="remove-user" on:click={() => removeUser(user)}>Remove</button>
        {:else}
            <span class="italic">No authorized users</span>
        {/each}
    </div>
</div>

<div class="add-user">
    <label class="add-user-email">
        Email
        <input type="email" autocomplete="off" bind:value={newEmail}>
    </label>
    
    <label>
        Role
        <select bind:value={newRole}>
            <option value="Viewer">Viewer</option>
            <option value="Administrator">Administrator</option>
        </select>
    </label>

</div>
<button class="add-user-button" on:click={addUser}>Add user</button>

<style>
    :global(main) {
        max-width: 650px;
    }

    .users-list {
        padding: 0.5em;
    }

    .users-list h1 {
        margin-top: 0;
    }

    .users-grid {
        display: grid;
        align-items: center;
        grid-template-columns: 1fr auto auto;
        gap: 1em;
    }

    .remove-user {
        color: white;
        background-color: #491d1d;
    }

    .remove-user:hover {
        background-color: #930c0c;
    }
    
    .italic {
        font-style: italic;
    }
    
    .add-user {
        margin-block: 1em;
        padding: 0.5em;
        display: flex;
        gap: 2em;
    }

    .add-user-email {
        flex: 1;
    }

    input:invalid {
        outline: 1px solid red;
    }

    .add-user-button {
        margin: 0.5em;
        color: white;
        background-color: #395aa3;
    }

    label {
        user-select: none;
        display: flex;
        flex-direction: column;
        gap: 0.5em;
    }

    input {
        font-size: 16px;
        font-family: consolas, monospace;
        color: rgb(168, 168, 168);
        background-color: rgb(28, 33, 44);
        border: none;
        padding: 0.5em;
    }

    select {
        color: rgb(168, 168, 168);
        font-size: 16px;
        padding: 0.5em;
        background-color: rgb(28, 33, 44);
        border: none;
    }
</style>