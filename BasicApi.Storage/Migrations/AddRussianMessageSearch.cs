using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Поиск по сообщениям с русским словарём.
///
/// Раньше и индекс, и запросы использовали 'english': русские слова не приводились к
/// основе, и «запуск» не находил «запускаем». Конфигурация 'russian' разбирает русские
/// слова русским стеммером, а латиницу — английским, так что смешанные сообщения
/// ищутся по обоим языкам.
///
/// search_vector — генерируемая колонка: её не нужно заполнять в коде, и запрос
/// не пересчитывает to_tsvector для каждой строки. Запросы обязаны использовать ту же
/// конфигурацию ('russian'), иначе основы слов не совпадут.
///
/// Добавление STORED-колонки переписывает таблицу messages (см. docs/deploy.md).
/// </summary>
[Migration(12)]
public class AddRussianMessageSearch : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            ALTER TABLE messages
            ADD COLUMN search_vector tsvector GENERATED ALWAYS AS (to_tsvector('russian', text)) STORED");
        Execute.Sql("CREATE INDEX ix_messages_search_vector ON messages USING GIN (search_vector)");
        Execute.Sql("DROP INDEX IF EXISTS ix_messages_search_gin");
    }

    public override void Down()
    {
        Execute.Sql("CREATE INDEX IF NOT EXISTS ix_messages_search_gin ON messages USING GIN (to_tsvector('english', text))");
        Execute.Sql("DROP INDEX IF EXISTS ix_messages_search_vector");
        Execute.Sql("ALTER TABLE messages DROP COLUMN search_vector");
    }
}
