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
  });

  final int processed;
  final int synced;
  final int failed;
  final int conflicts;
  final int remaining;
  final bool stoppedByNetwork;

  bool get hasErrors =>
      failed > 0 || conflicts > 0 || stoppedByNetwork;
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
    var conflicts = 0;
    var stoppedByNetwork = false;

    for (final item in pending) {
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
        );
        processed++;
        failed++;
      } on ApiException catch (error) {
        await localDatabase.markOutboxFailed(
          outboxId: item.id,
          error: error.message,
        );

        if (item.entityType == 'DetailAccount') {
          await localDatabase.markDetailAccountSyncError(
            entityId: item.entityId,
            error: error.message,
          );
        }

        if (item.entityType.startsWith('Store')) {
          await localDatabase.markStoreDraftSyncError(
            localId: item.entityId,
            error: error.message,
          );
        }

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

        if (item.entityType == 'DetailAccount') {
          await localDatabase.markDetailAccountSyncError(
            entityId: item.entityId,
            error: error.toString(),
          );
        }

        if (item.entityType.startsWith('Store')) {
          await localDatabase.markStoreDraftSyncError(
            localId: item.entityId,
            error: error.toString(),
          );
        }

        processed++;
        failed++;
      }
    }

    final remaining = await localDatabase.pendingOutboxCount();

    return SyncRunResult(
      processed: processed,
      synced: synced,
      failed: failed,
      conflicts: conflicts,
      remaining: remaining,
      stoppedByNetwork: stoppedByNetwork,
    );
  }
}
