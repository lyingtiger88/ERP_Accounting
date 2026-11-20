import 'package:flutter_test/flutter_test.dart';
import 'package:zarr/src/core/sync/outbox_sync_service.dart';

void main() {
  test('accepts a fully reconciled successful run', () {
    const result = SyncRunResult(
      processed: 5,
      synced: 5,
      failed: 0,
      conflicts: 0,
      remaining: 0,
      stoppedByNetwork: false,
    );

    expect(result.hasErrors, isFalse);
  });

  test('reports failed and conflicted operations', () {
    const result = SyncRunResult(
      processed: 4,
      synced: 2,
      failed: 1,
      conflicts: 1,
      remaining: 3,
      stoppedByNetwork: false,
    );

    expect(result.hasErrors, isTrue);
  });

  test('reports authentication interruption separately from network failure', () {
    const result = SyncRunResult(
      processed: 1,
      synced: 0,
      failed: 1,
      conflicts: 0,
      remaining: 4,
      stoppedByNetwork: false,
      stoppedByAuthentication: true,
    );

    expect(result.stoppedByAuthentication, isTrue);
    expect(result.stoppedByNetwork, isFalse);
    expect(result.hasErrors, isTrue);
  });

  test('rejects inconsistent processed counters', () {
    expect(
      () => SyncRunResult(
        processed: 3,
        synced: 1,
        failed: 1,
        conflicts: 0,
        remaining: 0,
        stoppedByNetwork: false,
      ),
      throwsAssertionError,
    );
  });

  test('rejects negative counters', () {
    expect(
      () => SyncRunResult(
        processed: -1,
        synced: -1,
        failed: 0,
        conflicts: 0,
        remaining: 0,
        stoppedByNetwork: false,
      ),
      throwsAssertionError,
    );
  });
}
