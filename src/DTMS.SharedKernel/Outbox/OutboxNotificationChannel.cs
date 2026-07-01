namespace DTMS.SharedKernel.Outbox;

/// <summary>
/// Phase O2 — single source of truth for the LISTEN/NOTIFY channel name
/// per module schema and the SQL fragments that install / drop the
/// AFTER-INSERT trigger. Consumed by:
/// <list type="bullet">
///   <item>Migration files (one per module DbContext) — install trigger</item>
///   <item><c>OutboxListenerService</c> — <c>LISTEN &lt;channel&gt;</c></item>
/// </list>
///
/// <para><b>Statement-level trigger — deliberate.</b> A batched INSERT
/// of N rows fires the trigger once, not N times. That's the semantic
/// we want: "there is at least one new row; go drain." Per-row triggers
/// would flood the notification queue and buy nothing.</para>
///
/// <para><b>AFTER INSERT commit semantics.</b> Postgres queues NOTIFY
/// payloads until commit, then delivers to listeners. So "listener wakes
/// up ⇒ row is visible to SELECT" is guaranteed. An EF SaveChanges
/// interceptor would fire pre-commit and race the listener.</para>
/// </summary>
public static class OutboxNotificationChannel
{
    /// <summary>Postgres NOTIFY channel name for the given schema.</summary>
    public static string ForSchema(string schema) => $"outbox_notify_{schema}";

    /// <summary>
    /// Idempotent SQL to install the notify function + AFTER INSERT trigger
    /// on the schema's <c>OutboxMessages</c> table. Safe to run in a
    /// migration Up() — <c>CREATE OR REPLACE FUNCTION</c> + <c>DROP TRIGGER
    /// IF EXISTS</c> keep it re-runnable.
    /// </summary>
    public static string InstallTriggerSql(string schema, string table = "OutboxMessages")
    {
        var channel = ForSchema(schema);
        return $$"""
            CREATE OR REPLACE FUNCTION "{{schema}}".notify_outbox_msg()
                RETURNS trigger
                LANGUAGE plpgsql
            AS $fn$
            BEGIN
                PERFORM pg_notify('{{channel}}', '');
                RETURN NULL;
            END;
            $fn$;

            DROP TRIGGER IF EXISTS outbox_notify ON "{{schema}}"."{{table}}";

            CREATE TRIGGER outbox_notify
                AFTER INSERT ON "{{schema}}"."{{table}}"
                FOR EACH STATEMENT
                EXECUTE FUNCTION "{{schema}}".notify_outbox_msg();
            """;
    }

    /// <summary>SQL to drop trigger + function. Migration Down().</summary>
    public static string UninstallTriggerSql(string schema, string table = "OutboxMessages")
    {
        return $"""
            DROP TRIGGER IF EXISTS outbox_notify ON "{schema}"."{table}";
            DROP FUNCTION IF EXISTS "{schema}".notify_outbox_msg();
            """;
    }
}
