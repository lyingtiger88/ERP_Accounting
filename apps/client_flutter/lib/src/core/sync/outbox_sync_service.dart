import '../api/api_client.dart';
import '../database/local_database.dart';

class SyncRunResult {
  const SyncRunResult({
    required this.processed,
    required this.synced,
    required this.failed,
    required this.conflicts,
    required this.remaining,
    required this.stoppedByNetwork,
    this.stoppedByAuthentication = false,
  })  : assert(processed >= 0),
        assert(synced >= 0),
        assert(failed >= 0),
        assert(conflicts >= 0),
        assert(remaining >= 0),
        assert(synced + failed + conflicts == processed);

  final int processed;
  final int synced;
  final int failed;
  final int conflicts;
  final int remaining;
  final bool stoppedByNetwork;
  final bool stoppedByAuthentication;

  bool get hasErrors =>
      failed > 0 ||
      conflicts > 0 ||
      stoppedByNetwork ||
      stoppedByAuthentication;
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
    final safeBatchSize = batchSize.clamp(1, 200).toInt();
    final pending = await localDatabase.claimPendingOutbox(
      limit: safeBatchSize,
    );

    var processed = 0;
    var synced = 0;
    var failed = 0;
    var conflicts = 0;
    var stoppedByNetwork = false;
    var stoppedByAuthentication = false;

    for (var index = 0; index < pending.length; index++) {
      final item = pending[index];
      try {
        if (item.entityType == 'AccountingDocument' &&
            item.operation == 'Create') {
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
          continue;
        }

        if (item.entityType == 'DetailAccount' &&
            item.operation == 'Upsert') {
          final result = await apiClient.syncDetailAccount(
            bearerToken: bearerToken,
            changeId: item.changeId,
            payload: item.payload,
          );

          if (result.applied && result.entity != null) {
            await localDatabase.markDetailAccountOutboxSent(
              outboxId: item.id,
              serverEntity: result.entity!,
            );
            processed++;
            synced++;
            continue;
          }

          if (result.conflict) {
            await localDatabase.recordDetailAccountConflict(
              item: item,
              serverEntity: result.serverConflict,
            );
            processed++;
            conflicts++;
            continue;
          }

          throw StateError(
            'Unexpected detail-account sync outcome: ' +
                result.outcome,
          );
        }

        if (item.operation == 'Create' &&
            item.entityType == 'StoreSalesInvoiceDraft') {
          final payload = item.payload;

          final result = await apiClient.createSalesInvoice(
            bearerToken: bearerToken,
            fiscalYearId: payload['fiscalYearId'].toString(),
            documentDate:
                DateTime.parse(payload['documentDate'].toString()),
            warehouseId: payload['warehouseId'].toString(),
            customerDetailAccountId:
                payload['customerDetailAccountId']?.toString(),
            paymentType: payload['paymentType'].toString(),
            description: payload['description']?.toString(),
            currencyId: payload['currencyId']?.toString(),
            exchangeRate: payload['exchangeRate'] as num?,
            idempotencyKey: item.changeId,
            lines: (payload['lines'] as List<dynamic>)
                .map(
                  (line) => Map<String, dynamic>.from(line as Map),
                )
                .toList(growable: false),
          );

          await localDatabase.markStoreDraftSynced(
            outboxId: item.id,
            localId: item.entityId,
            serverEntity: result,
          );

          processed++;
          synced++;
          continue;
        }

        if (item.operation == 'Create' &&
            item.entityType == 'StorePurchaseReceiptDraft') {
          final payload = item.payload;

          final result = await apiClient.createPurchaseReceipt(
            bearerToken: bearerToken,
            fiscalYearId: payload['fiscalYearId'].toString(),
            documentDate:
                DateTime.parse(payload['documentDate'].toString()),
            warehouseId: payload['warehouseId'].toString(),
            supplierDetailAccountId:
                payload['supplierDetailAccountId']?.toString(),
            paymentType: payload['paymentType'].toString(),
            description: payload['description']?.toString(),
            currencyId: payload['currencyId']?.toString(),
            exchangeRate: payload['exchangeRate'] as num?,
            purchaseOrderId: payload['purchaseOrderId']?.toString(),
            idempotencyKey: item.changeId,
            lines: (payload['lines'] as List<dynamic>)
                .map(
                  (line) => Map<String, dynamic>.from(line as Map),
                )
                .toList(growable: false),
          );

          await localDatabase.markStoreDraftSynced(
            outboxId: item.id,
            localId: item.entityId,
            serverEntity: result,
          );

          processed++;
          synced++;
          continue;
        }

        if (item.operation == 'Create' &&
            item.entityType == 'StoreWarehouseTransferDraft') {
          final payload = item.payload;

          final result = await apiClient.createWarehouseTransfer(
            bearerToken: bearerToken,
            documentDate:
                DateTime.parse(payload['documentDate'].toString()),
            fromWarehouseId:
                payload['fromWarehouseId'].toString(),
            toWarehouseId:
                payload['toWarehouseId'].toString(),
            description: payload['description']?.toString(),
            idempotencyKey: item.changeId,
            lines: (payload['lines'] as List<dynamic>)
                .map(
                  (line) => Map<String, dynamic>.from(line as Map),
                )
                .toList(growable: false),
          );

          await localDatabase.markStoreDraftSynced(
            outboxId: item.id,
            localId: item.entityId,
            serverEntity: result,
          );

          processed++;
          synced++;
          continue;
        }

        await localDatabase.markOutboxFailed(
          outboxId: item.id,
          error:
              'Unsupported outbox operation: ' +
              item.entityType +
              '/' +
              item.operation,
          retryable: false,
          terminalStatus: 'Rejected',
        );
        processed++;
        failed++;
      } on ApiException catch (error) {
        final statusCode = error.statusCode;
        final retryable = statusCode == null ||
            statusCode == 408 ||
            statusCode == 425 ||
            statusCode == 429 ||
            statusCode >= 500;
        final terminalStatus = statusCode == 401 || statusCode == 403
            ? 'Blocked'
            : statusCode == 409
                ? 'Conflict'
                : 'Rejected';

        final outboxStatus = await localDatabase.markOutboxFailed(
          outboxId: item.id,
          error: error.message,
          retryable: retryable,
          terminalStatus: terminalStatus,
        );

        if (item.entityType == 'DetailAccount') {
          await localDatabase.markDetailAccountSyncError(
            entityId: item.entityId,
            error: error.message,
            syncStatus: outboxStatus ?? 'Pending',
          );
        }

        if (item.entityType.startsWith('Store')) {
          await localDatabase.markStoreDraftSyncError(
            localId: item.entityId,
            error: error.message,
            syncStatus: outboxStatus ?? 'Pending',
          );
        }

        processed++;
        if (statusCode == 409) {
          conflicts++;
        } else {
          failed++;
        }

        if (statusCode == null ||
            statusCode == 401 ||
            statusCode == 403) {
          stoppedByNetwork = statusCode == null;
          stoppedByAuthentication = statusCode == 401 || statusCode == 403;
          // The whole batch was claimed before processing. Release untouched
          // rows immediately instead of stranding them in Processing until
          // their leases expire.
          for (final unprocessed in pending.skip(index + 1)) {
            await localDatabase.releaseOutboxClaim(unprocessed.id);
          }
          break;
        }
      } catch (error) {
        // Unknown failures may happen after the server committed but before the
        // local acknowledgement was persisted. Retry with the same ChangeId:
        // server-side idempotency makes that recovery safe.
        final outboxStatus = await localDatabase.markOutboxFailed(
          outboxId: item.id,
          error: error.toString(),
          retryable: true,
        );

        if (item.entityType == 'DetailAccount') {
          await localDatabase.markDetailAccountSyncError(
            entityId: item.entityId,
            error: error.toString(),
            syncStatus: outboxStatus ?? 'Pending',
          );
        }

        if (item.entityType.startsWith('Store')) {
          await localDatabase.markStoreDraftSyncError(
            localId: item.entityId,
            error: error.toString(),
            syncStatus: outboxStatus ?? 'Pending',
          );
        }

        processed++;
        failed++;
      }
    }

    final remaining = await localDatabase.unresolvedOutboxCount();

    return SyncRunResult(
      processed: processed,
      synced: synced,
      failed: failed,
      conflicts: conflicts,
      remaining: remaining,
      stoppedByNetwork: stoppedByNetwork,
      stoppedByAuthentication: stoppedByAuthentication,
    );
  }
}
