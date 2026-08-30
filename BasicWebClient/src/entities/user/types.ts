/* Типы пользователя. Пишем руками по DTO бэкенда (BasicApi/Models/Dto/Users).
   Правило: имена полей — ровно как в JSON (camelCase). */

/** Публичный профиль: то, что можно показать любому авторизованному. */
export interface UserProfile {
  userId: string
  username: string
  displayName: string
}

/** Свой профиль — то же плюс email. GET /api/users/me */
export interface OwnProfile extends UserProfile {
  email: string
}

/** Результат поиска. Такой же, как UserProfile, плюс необязательная аватарка. */
export interface UserSearchResult extends UserProfile {
  avatarUrl?: string | null
}

export interface SearchUsersResponse {
  items: UserSearchResult[]
  query: string
  totalCount: number
}

export interface UserStatus {
  userId: string
  isOnline: boolean
}

export interface UserStatusResponse {
  items: UserStatus[]
}

export interface TypingStatus {
  userId: string
  chatId: string
  isTyping: boolean
}

export interface TypingStatusResponse {
  items: TypingStatus[]
}

export interface UserIdResponse {
  userId: string
}
