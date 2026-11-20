import 'package:flutter_test/flutter_test.dart';
import 'package:sqflite_common_ffi/sqflite_ffi.dart';
import 'package:zarr/src/core/database/local_database.dart';

void main() {
  setUpAll(() {
    sqfliteFfiInit();
  });

  Future<({Database database, LocalDatabase local})> createDatabase() async {
    final database = await databaseFactoryFfi.openDatabase(inMemoryDatabasePath);
    await database.execute('''
      CREATE TABLE sync_outbox (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        change_id TEXT NOT NULL UNIQUE,
        company_id TEXT NOT NULL,
        entity_type TEXT NOT NULL,
        entity_id TEXT NOT NULL,
        operation TEXT NOT NULL,
        payload_json TEXT NOT NULL,
        created_at TEXT NOT NULL,
        attempt_count INTEGER NOT NULL DEFAULT 0,
        last_error TEXT,
        sent_at TEXT,
        status TEXT NOT NULL DEFAULT 'Pending',
        next_attempt_at TEXT,
        last_attempt_at TEXT
      )
    ''');
    return (
      database: database,
      local: LocalDatabase.forTesting(database),
    );
  }

  Future<int> insertOutbox(Database database, String changeId) {
    return database.insert('sync_outbox', {
      'change_id': changeId,
      'company_id': 'company-1',
      'entity_type': 'StoreSalesInvoiceDraft',
      'entity_id': 'draft-1',
      'operation': 'Create',
      'payload_json': '{}',
      'created_at': DateTime.now().toUtc().toIso8601String(),
      'attempt_count': 0,
      'status': 'Pending',
    });
  }

  test('transient failures are scheduled and excluded until due', () async {
    final fixture = await createDatabase();
    addTearDown(fixture.database.close);
    final id = await insertOutbox(fixture.database, 'change-transient');

    await fixture.local.markOutboxFailed(
      outboxId: id,
      error: 'temporary server failure',
      retryable: true,
    );

    final row = (await fixture.database.query(
      'sync_outbox',
      where: 'id = ?',
      whereArgs: [id],
    )).single;

    expect(row['status'], 'Retry');
    expect(row['attempt_count'], 1);
    expect(row['next_attempt_at'], isNotNull);
    expect(await fixture.local.getPendingOutbox(), isEmpty);
    expect(await fixture.local.pendingOutboxCount(), 1);
    expect(await fixture.local.unresolvedOutboxCount(), 1);
  });

  test('permanent failures become terminal and are not auto-retried', () async {
    final fixture = await createDatabase();
    addTearDown(fixture.database.close);
    final id = await insertOutbox(fixture.database, 'change-invalid');

    await fixture.local.markOutboxFailed(
      outboxId: id,
      error: 'unsupported entity operation',
      retryable: false,
      terminalStatus: 'Rejected',
    );

    final row = (await fixture.database.query(
      'sync_outbox',
      where: 'id = ?',
      whereArgs: [id],
    )).single;

    expect(row['status'], 'Rejected');
    expect(await fixture.local.getPendingOutbox(), isEmpty);
    expect(await fixture.local.pendingOutboxCount(), 0);
    expect(await fixture.local.unresolvedOutboxCount(), 1);
  });

  test('dead-letter items can be manually retried with a fresh retry budget', () async {
    final fixture = await createDatabase();
    addTearDown(fixture.database.close);
    final id = await insertOutbox(fixture.database, 'change-dead-letter');

    await fixture.local.markOutboxFailed(
      outboxId: id,
      error: 'retry budget exhausted',
      retryable: true,
      maxAttempts: 1,
    );

    var row = (await fixture.database.query(
      'sync_outbox',
      where: 'id = ?',
      whereArgs: [id],
    )).single;
    expect(row['status'], 'DeadLetter');
    expect(row['attempt_count'], 1);

    await fixture.local.retryOutboxItem(id);

    row = (await fixture.database.query(
      'sync_outbox',
      where: 'id = ?',
      whereArgs: [id],
    )).single;
    expect(row['status'], 'Pending');
    expect(row['attempt_count'], 0);
    expect(row['next_attempt_at'], isNull);
    expect(await fixture.local.getPendingOutbox(), hasLength(1));
  });

  test('blocked work is eligible on the next authenticated sync attempt', () async {
    final fixture = await createDatabase();
    addTearDown(fixture.database.close);
    final id = await insertOutbox(fixture.database, 'change-blocked');

    await fixture.local.markOutboxFailed(
      outboxId: id,
      error: 'session needs re-authentication',
      retryable: false,
      terminalStatus: 'Blocked',
    );

    expect(await fixture.local.getPendingOutbox(), hasLength(1));
  });
}
