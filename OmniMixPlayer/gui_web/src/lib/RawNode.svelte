<script lang="ts">
  import type { RawNodeData, UiKind } from "./types";
  import { normalizeColor } from "./util";

  export let node: RawNodeData;
  export let moduleId: string;
  export let uiKind: UiKind = "default";
  export let linkId = "";
  export let dispatchEvent: (
    moduleId: string,
    nodeId: string,
    action: string,
    value: string,
    uiKind: UiKind,
    linkId: string
  ) => Promise<void>;

  let draft = node.value ?? "";
  let lastNode: RawNodeData | null = null;

  $: nodeType = node["node-type"] ?? "";
  $: children = node.children ?? [];
  $: items = node.items ?? [];
  $: options = node.options ?? [];
  $: text = node.text ?? "";
  $: nodeId = node.id ?? "";
  $: nodeColor = normalizeColor(node.color);
  $: if (node !== lastNode) {
    draft = node.value ?? "";
    lastNode = node;
  }

  function nodeStyle(nodeData: RawNodeData): string {
    const parts: string[] = [];
    const padding = nodeData.padding ?? 0;
    const spacing = nodeData.spacing ?? 8;
    const color = normalizeColor(nodeData.color);
    if (padding > 0) parts.push(`padding:${padding}px`);
    if (spacing >= 0) parts.push(`gap:${spacing}px`);
    if (color && nodeData["node-type"] === "Container") parts.push(`background:${color}`);
    return parts.join(";");
  }

  function imageUrl(source?: string): string {
    if (!source) return "";
    return source;
  }

  function dispatch(action: string, value = "") {
    if (!nodeId) return;
    void dispatchEvent(moduleId, nodeId, action, value, uiKind, linkId);
  }
</script>

{#if nodeType === "Container"}
  <div
    class:horizontal={node.direction === "Horizontal"}
    class="raw-container"
    style={nodeStyle(node)}
  >
    {#each children as child}
      <svelte:self
        node={child}
        {moduleId}
        {uiKind}
        {linkId}
        {dispatchEvent}
      />
    {/each}
  </div>
{:else if nodeType === "Text"}
  <p class="raw-text" style={`font-size:${node["font-size"] ?? 14}px;${nodeColor ? `color:${nodeColor}` : ""}`}>
    {text}
  </p>
{:else if nodeType === "Input"}
  <label class="raw-field">
    <span>{text}</span>
    <input
      bind:value={draft}
      type={node["input-type"] === "password" ? "password" : "text"}
      on:blur={() => dispatch("change", draft)}
      on:keydown={(event) => {
        if (event.key === "Enter") dispatch("change", draft);
      }}
    />
  </label>
{:else if nodeType === "Button"}
  <button
    class:danger={node["button-variant"] === "danger"}
    class:secondary={!node["button-variant"]}
    class="raw-button"
    type="button"
    on:click={() => dispatch("click", node.value ?? "")}
  >
    {text}
  </button>
{:else if nodeType === "ExternalLink"}
  <a class="raw-link" href={node.value || node.source} target="_blank" rel="noreferrer" on:click={() => dispatch("open", node.value ?? node.source ?? "")}>
    {text || node.value || node.source}
  </a>
{:else if nodeType === "Switch"}
  <label class="raw-switch">
    <span>{text}</span>
    <input
      type="checkbox"
      checked={node.checked ?? false}
      on:change={(event) => dispatch("toggle", String((event.currentTarget as HTMLInputElement).checked))}
    />
  </label>
{:else if nodeType === "Image"}
  {#if node.source}
    <img
      class="raw-image"
      src={imageUrl(node.source)}
      alt={node.id ?? ""}
      style={`width:${node["image-width"] ?? 200}px;height:${node["image-height"] ?? 200}px;object-fit:${node["image-fit"] ?? "contain"}`}
    />
  {/if}
{:else if nodeType === "Select"}
  <label class="raw-field compact">
    <span>{text}</span>
    <select
      value={node["selected-value"] ?? ""}
      on:change={(event) => dispatch("change", (event.currentTarget as HTMLSelectElement).value)}
    >
      {#each options as option}
        <option value={option.value}>{option.label}</option>
      {/each}
    </select>
  </label>
{:else if nodeType === "List"}
  <div class="raw-list">
    {#each items as item}
      <svelte:self
        node={item}
        {moduleId}
        {uiKind}
        {linkId}
        {dispatchEvent}
      />
    {/each}
  </div>
{/if}
