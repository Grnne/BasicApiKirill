<script setup lang="ts">
import { nextTick, onUnmounted, ref, watch } from 'vue'

/** The page's menu: new group, saved messages, settings, logout — named, not guessed from icons. */
const emit = defineEmits<{ group: []; saved: []; settings: []; logout: [] }>()

const open = ref(false)
const toggle = ref<HTMLButtonElement | null>(null)
const menu = ref<HTMLElement | null>(null)

function close(): void {
  open.value = false
}

// Any click elsewhere closes it.
watch(open, async (isOpen) => {
  if (!isOpen) {
    document.removeEventListener('click', close)
    return
  }
  setTimeout(() => document.addEventListener('click', close, { once: true }))
  await nextTick()
  menu.value?.querySelector<HTMLElement>('[role=menuitem]')?.focus()
})
onUnmounted(() => document.removeEventListener('click', close))

function escape(): void {
  close()
  toggle.value?.focus()
}

function pick(action: 'group' | 'saved' | 'settings' | 'logout'): void {
  close()
  if (action === 'group') emit('group')
  else if (action === 'saved') emit('saved')
  else if (action === 'settings') emit('settings')
  else emit('logout')
}
</script>

<template>
  <div class="main-menu">
    <button
      ref="toggle"
      type="button"
      class="toggle"
      title="Меню"
      aria-label="Меню"
      aria-haspopup="menu"
      :aria-expanded="open"
      @click.stop="open = !open"
    >
      ☰
    </button>
    <ul v-if="open" ref="menu" class="menu" role="menu" @click.stop @keydown.esc.stop="escape">
      <li><button type="button" role="menuitem" @click="pick('group')">👥 Новая группа</button></li>
      <li><button type="button" role="menuitem" @click="pick('saved')">★ Избранное</button></li>
      <li><button type="button" role="menuitem" @click="pick('settings')">⚙ Настройки</button></li>
      <li class="separated"><button type="button" role="menuitem" class="danger" @click="pick('logout')">Выйти</button></li>
    </ul>
  </div>
</template>

<style scoped>
.main-menu {
  position: relative;
  flex: none;
}
.toggle {
  width: 34px;
  height: 34px;
  padding: 0;
  border: none;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-dim);
  font-size: 18px;
}
.toggle:hover,
.toggle[aria-expanded='true'] {
  background: var(--surface-hover);
  color: var(--text);
}
.menu {
  position: absolute;
  top: calc(100% + 4px);
  left: 0;
  z-index: 50;
  min-width: 200px;
  margin: 0;
  padding: 4px;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--surface-solid);
  box-shadow: 0 4px 16px #0008;
  list-style: none;
}
.menu button {
  display: block;
  width: 100%;
  padding: 7px 10px;
  border: none;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text);
  text-align: left;
}
.menu button:hover,
.menu button:focus-visible {
  background: var(--surface-hover);
  outline: none;
}
.separated {
  margin-top: 4px;
  padding-top: 4px;
  border-top: 1px solid var(--border);
}
.menu .danger {
  color: var(--danger);
}
</style>
