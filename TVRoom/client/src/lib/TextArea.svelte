<script lang="ts">
    import { afterUpdate, createEventDispatcher, onDestroy, onMount } from "svelte";
    import autosize from "autosize";
  
    export let value: string, 
      placeholder = '',
      preventNewLine = false;
  
 
    let textarea: HTMLTextAreaElement;
  
    const dispatch = createEventDispatcher();
  
    function handleKeyDown(event: KeyboardEvent) {
      if (event.keyCode === 13) { // enter key 
        const textBeforeCursor = value.slice(0, textarea.selectionStart);
        const textAfterCursor = value.slice(textarea.selectionStart);
        dispatch('enter', { textBeforeCursor, textAfterCursor });
        if (preventNewLine) {
          event.preventDefault();
        }
      } else if (event.keyCode === 8) { // backspace key
        if (textarea.selectionStart === 0 && textarea.selectionEnd === 0) {
          dispatch('backspaceAtStart');
          event.preventDefault();
        }
      }
    }
  
    onMount(() => autosize(textarea));
    // @ts-ignore
    afterUpdate(() => autosize.update(textarea));
    // @ts-ignore
    onDestroy(() => autosize.destroy(textarea));
  </script>
  
  <textarea 
    class={$$props.class} 
    rows="1" 
    {placeholder} 
    bind:value 
    bind:this={textarea} 
    on:keydown={handleKeyDown}
    on:paste
    ></textarea>

<style>
  textarea {
      font-size: 16px;
      font-family: consolas, monospace;
      color: rgb(168, 168, 168);
      background-color: rgb(28, 33, 44);
      border: none;
      padding: 0.5em;
      resize: none;
  }
</style>