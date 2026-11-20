import 'package:flutter_test/flutter_test.dart';
import 'package:sqflite_common/sqlite_api.dart';
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
        last_attempt_at TEXT,
        lease_expires_at TEXT
      )
    ''');
    await database.execute('''
      CREATE TABLE local_store_drafts (
        id TEXT PRIMARY KEY,
        sync_status TEXT NOT NULL,
        last_error TEXT,
        server_id TEXT,
        server_number TEXT,
        updated_at TEXT NOT NULL
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
    final firstRetryAt = DateTime.parse(row['next_attempt_at'] as String);
    expect(
      firstRetryAt.isAfter(
        DateTime.now().toUtc().add(const Duration(seconds: 29)),
      ),
      isTrue,
    );
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

  test('retry budget exhaustion moves an item to dead letter', () async {
    final fixture = await createDatabase();
    addTearDown(fixture.database.close);
    final id = await insertOutbox(fixture.database, 'change-exhausted');

    String? status;
    for (var attempt = 0; attempt < 8; attempt++) {
      status = await fixture.local.markOutboxFailed(
        outboxId: id,
        error: 'temporary failure',
        retryable: true,
      );
    }

    final row = (await fixture.database.query(
      'sync_outbox',
      where: 'id = ?',
      whereArgs: [id],
    )).single;
    expect(status, 'DeadLetter');
    expect(row['status'], 'DeadLetter');
    expect(row['attempt_count'], 8);
    expect(row['next_attempt_at'], isNull);
  });

  test('successful store acknowledgement atomically marks draft and Outbox sent', () async {
    final fixture = await createDatabase();
    addTearDown(fixture.database.close);
    final id = await insertOutbox(fixture.database, 'change-ack');
    await fixture.database.insert('local_store_drafts', {
      'id': 'draft-ack',
      'sync_status': 'Pending',
      'last_error': null,
      'updated_at': DateTime.now().toUtc().toIso8601String(),
    });

    await fixture.local.claimPendingOutbox();
    await fixture.local.markStoreDraftSynced(
      outboxId: id,
      localId: 'draft-ack',
      serverEntity: {'id': 'server-document', 'number': 'INV-001'},
    );

    final outbox = (await fixture.database.query(
      'sync_outbox',
      where: 'id = ?',
      whereArgs: [id],
    )).single;
    final draft = (await fixture.database.query(
      'local_store_drafts',
      where: 'id = ?',
      whereArgs: ['draft-ack'],
    )).single;

    expect(outbox['status'], 'Sent');
    expect(outbox['sent_at'], isNotNull);
    expect(outbox['lease_expires_at'], isNull);
    expect(draft['sync_status'], 'Synced');
    expect(draft['server_id'], 'server-document');
    expect(draft['server_number'], 'INV-001');
  });

  test('local store draft exposes blocked and conflict states', () async {
    final fixture = await createDatabase();
    addTearDown(fixture.database.close);
    await fixture.database.insert('local_store_drafts', {
      'id': 'draft-status',
      'sync_status': 'Pending',
      'last_error': null,
      'updated_at': DateTime.now().toUtc().toIso8601String(),
    });

    await fixture.local.markStoreDraftSyncError(
      localId: 'draft-status',
      error: 'authentication required',
      syncStatus: 'Blocked',
    );
    var row = (await fixture.database.query(
      'local_store_drafts',
      where: 'id = ?',
      whereArgs: ['draft-status'],
    )).single;
    expect(row['sync_status'], 'Blocked');

    await fixture.local.markStoreDraftSyncError(
      localId: 'draft-status',
      error: 'document changed on server',
      syncStatus: 'Conflict',
    );
    row = (await fixture.database.query(
      'local_store_drafts',
      where: 'id = ?',
      whereArgs: ['draft-status'],
    )).single;
    expect(row['sync_status'], 'Conflict');
  });

  test('claims are exclusive and expired processing leases are recoverable', () async {
    final fixture = await createDatabase();
    addTearDown(fixture.database.close);
    final id = await insertOutbox(fixture.database, 'change-lease');

    final firstClaim = await fixture.local.claimPendingOutbox();
    expect(firstClaim, hasLength(1));
    expect(firstClaim.single.id, id);

    final processingRow = (await fixture.database.query(
      'sync_outbox',
      where: 'id = ?',
      whereArgs: [id],
    )).single;
    expect(processingRow['status'], 'Processing');
    expect(processingRow['lease_expires_at'], isNotNull);

    final secondClaim = await fixture.local.claimPendingOutbox();
    expect(secondClaim, isEmpty);

    final failureStatus = await fixture.local.markOutboxFailed(
      outboxId: id,
      error: 'transient after claim',
      retryable: true,
    );
    expect(failureStatus, 'Retry');

    final releasedRow = (await fixture.database.query(
      'sync_outbox',
      where: 'id = ?',
      whereArgs: [id],
    )).single;
    expect(releasedRow['lease_expires_at'], isNull);
    expect(releasedRow['status'], 'Retry');

    await fixture.database.update(
      'sync_outbox',
      {
        'lease_expires_at': DateTime.now()
            .toUtc()
            .subtract(const Duration(seconds: 1))
            .toIso8601String(),
      },
      where: 'id = ?',
      whereArgs: [id],
    );

    final recoveredClaim = await fixture.local.claimPendingOutbox();
    expect(recoveredClaim, hasLength(1));
    expect(recoveredClaim.single.changeId, 'change-lease');
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
