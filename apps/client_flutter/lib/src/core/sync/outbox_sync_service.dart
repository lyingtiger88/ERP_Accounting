import '../api/api_client.dart';
import '../database/local_database.dart';

class SyncRunResult {
  const SyncRunResult({
    required this.processed,
    required this.synced,
    required this.failed,
    required this.remaining,
    required this.stoppedByNetwork,
  });

  final int processed;
  final int synced;
  final int failed;
  final int remaining;
  final bool stoppedByNetwork;

  bool get hasErrors => failed > 0 || stoppedByNetwork;
}

class OutboxSyncService {
  const OutboxSyncService({
    required this.localDatabase,
    required this.apiClient,
  });

  final LocalDatabase localDatabase;
  final ApiClient apiClient;

  Future<SyncRunResult> syncPending({
    required String bearerToken,
    int batchSize = 50,
  }) async {
    final pending = await localDatabase.getPendingOutbox(
      limit: batchSize,
    );

    var processed = 0;
    var synced = 0;
    var failed = 0;
    var stoppedByNetwork = false;

    for (final item in pending) {
      if (item.entityType != 'AccountingDocument' ||
          item.operation != 'Create') {
        await localDatabase.markOutboxFailed(
          outboxId: item.id,
          error:
              'Unsupported outbox operation: ' +
              item.entityType +
              '/' +
              item.operation,
        );
        processed++;
        failed++;
        continue;
      }

      try {
        final result = await apiClient.syncJournal(
          bearerToken: bearerToken,
          changeId: item.changeId,
          payload: item.payload,
        );

        await localDatabase.markOutboxSent(
          outboxId: item.id,
          localDocumentId: item.entityId,
          serverId: result.journalEntryId,
          serverNumber: result.number,
        );

        processed++;
        synced++;
      } on ApiException catch (error) {
        await localDatabase.markOutboxFailed(
          outboxId: item.id,
          error: error.message,
        );

        processed++;
        failed++;

        if (error.statusCode == null ||
            error.statusCode == 401 ||
            error.statusCode == 403) {
          stoppedByNetwork = error.statusCode == null;
          break;
        }
      } catch (error) {
        await localDatabase.markOutboxFailed(
          outboxId: item.id,
          error: error.toString(),
        );

        processed++;
        failed++;
      }
    }

    final remaining = await localDatabase.pendingOutboxCount();

    return SyncRunResult(
      processed: processed,
      synced: synced,
      failed: failed,
      remaining: remaining,
      stoppedByNetwork: stoppedByNetwork,
    );
  }
}
