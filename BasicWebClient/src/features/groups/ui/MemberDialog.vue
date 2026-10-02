<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref } from 'vue'

import * as chatApi from '@/entities/chat/api'
import { PERMISSION_LABELS, ROLE_LABELS, mayGive, memberActions, type Permission } from '@/entities/chat/members'
import { useChatDetailsStore } from '@/entities/chat/model/details.store'
import AvatarCircle from '@/entities/media/ui/AvatarCircle.vue'
import { describeError } from '@/shared/api/problem'
import type { GroupMemberDto, PermissionsPatchDto } from '@/shared/api/schema'
import ConfirmDialog from '@/shared/ui/ConfirmDialog.vue'
import { useRestoreFocus } from '@/shared/ui/useRestoreFocus'

const props = defineProps<{ chatId: string; meId: string; userId: string }>()
const emit = defineEmits<{ close: [] }>()
useRestoreFocus()

const details = useChatDetailsStore()

const detail = computed(() => details.get(props.chatId))
const participant = computed(() => details.member(props.chatId, props.userId))
const actions = computed(() =>
  detail.value && participant.value ? memberActions(detail.value, props.meId, participant.value) : null,
)

/** Off now and not the caller's to give: the box stays off, the server would refuse it. */
const locked = (p: Permission) => !member.value?.permissions[p] && !!detail.value && !mayGive(detail.value, p)

/** The member with what they may do now; only the members list carries it. */
const member = ref<GroupMemberDto | null>(null)
const shown = reactive<PermissionsPatchDto>({})
const busy = ref(false)
const error = ref<string | null>(null)

async function load(): Promise<void> {
  try {
    const all = await chatApi.getMembers(props.chatId)
    member.value = all.find((m) => m.userId === props.userId) ?? null
    if (member.value) Object.assign(shown, member.value.permissions)
  } catch (e) {
    error.value = describeError(e)
  }
}

function applied(updated: GroupMemberDto): void {
  member.value = updated
  Object.assign(shown, updated.permissions)
  details.apply('MemberUpdated', { chatId: props.chatId, member: updated }, props.meId)
}

async function run(action: () => Promise<GroupMemberDto>): Promise<void> {
  busy.value = true
  error.value = null
  try {
    applied(await action())
  } catch (e) {
    error.value = describeError(e)
  } finally {
    busy.value = false
  }
}

const setRole = (role: 'admin' | 'member') => run(() => chatApi.setRole(props.chatId, props.userId, role))

const handingOver = ref(false)
async function handOver(): Promise<void> {
  handingOver.value = false
  await run(() => chatApi.setRole(props.chatId, props.userId, 'owner'))
  // Both roles changed: MemberUpdated about the caller comes too; fetch the card to be sure now.
  await details.load(props.chatId)
}

// Overrides are replaced as a whole, so every permission shown is sent: they stop following the group.
const savePermissions = () =>
  run(() =>
    chatApi.setMemberPermissions(
      props.chatId,
      props.userId,
      Object.fromEntries((actions.value?.permissions ?? []).map((p) => [p, shown[p] ?? false])),
    ),
  )
const resetPermissions = () => run(() => chatApi.setMemberPermissions(props.chatId, props.userId, {}))

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape' && !handingOver.value) emit('close')
}
onMounted(() => {
  document.addEventListener('keydown', onKeydown)
  void load()
})
onUnmounted(() => document.removeEventListener('keydown', onKeydown))
</script>

<template>
  <div class="overlay" @click.self="emit('close')">
    <div class="dialog" role="dialog" aria-modal="true" aria-label="Участник">
      <div v-if="participant" class="who">
        <AvatarCircle :avatar-id="participant.avatarId" :initial="participant.displayName.charAt(0).toUpperCase() || '?'" :size="44" />
        <span class="name">
          <span>{{ participant.displayName }}</span>
          <span class="role">{{ ROLE_LABELS[participant.role] ?? 'участник' }}</span>
        </span>
      </div>
      <p v-else class="note">Участника больше нет в группе</p>

      <div v-if="actions" class="roles">
        <button v-if="actions.makeAdmin" type="button" class="button" :disabled="busy" @click="setRole('admin')">Сделать админом</button>
        <button v-if="actions.makeMember" type="button" class="button" :disabled="busy" @click="setRole('member')">Снять админа</button>
        <button v-if="actions.handOver" type="button" class="button" :disabled="busy" @click="handingOver = true">Передать группу</button>
      </div>

      <fieldset v-if="actions && actions.permissions.length > 0 && member" class="permissions">
        <legend class="label">Может</legend>
        <label v-for="p in actions.permissions" :key="p" class="check">
          <input v-model="shown[p]" type="checkbox" :disabled="busy || locked(p)" />
          {{ PERMISSION_LABELS[p] }}
        </label>
        <p class="hint">Сохранённые права больше не следуют настройкам группы.</p>
        <div class="row">
          <button type="button" class="button" :disabled="busy" @click="resetPermissions">Как в группе</button>
          <button type="button" class="button primary" :disabled="busy" @click="savePermissions">Сохранить права</button>
        </div>
      </fieldset>

      <p v-if="error" class="error">{{ error }}</p>
      <div class="buttons">
        <button type="button" class="button" @click="emit('close')">Закрыть</button>
      </div>
    </div>

    <ConfirmDialog
      v-if="handingOver && participant"
      title="Передать группу?"
      :text="`Владельцем станет ${participant.displayName}, вы станете админом.`"
      confirm-label="Передать"
      danger
      @confirm="handOver"
      @cancel="handingOver = false"
    />
  </div>
</template>

<style scoped>
.overlay {
  position: fixed;
  inset: 0;
  z-index: 90;
  display: grid;
  place-items: center;
  padding: 16px;
  background: #000a;
}
.dialog {
  display: grid;
  gap: 12px;
  width: min(380px, 100%);
  padding: 18px;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--surface-solid);
}
.who {
  display: flex;
  align-items: center;
  gap: 12px;
}
.name {
  display: grid;
  font-weight: 600;
}
.role,
.label,
.hint {
  color: var(--text-dim);
  font-size: 12px;
  font-weight: 400;
}
.hint {
  margin: 0;
}
.roles,
.row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.permissions {
  display: grid;
  gap: 4px;
  margin: 0;
  padding: 8px 10px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
}
.check {
  display: flex;
  align-items: center;
  gap: 8px;
}
.note {
  margin: 0;
  color: var(--text-dim);
}
.error {
  margin: 0;
  color: var(--danger);
  font-size: 12px;
}
.buttons {
  display: flex;
  justify-content: flex-end;
}
.button {
  padding: 6px 12px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text);
}
.button.primary {
  border-color: transparent;
  background: var(--accent);
  color: #04160b;
  font-weight: 600;
}
.button:disabled {
  opacity: 0.5;
}
</style>
