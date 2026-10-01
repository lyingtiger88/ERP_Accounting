import '../api/api_client.dart';
import '../database/local_database.dart';
import 'detail_account_pull_sync_service.dart';
import 'journal_pull_sync_service.dart';
import 'outbox_sync_service.dart';
import 'sales_inventory_cache_sync_service.dart';

class AccountingSyncRunResult {
  const AccountingSyncRunResult({
    required this.pushed,
    required this.pushFailed,
    required this.conflicts,
    required this.pulled,
    required this.remainingOutbox,
    required this.stoppedByNetwork,
    required this.startCursor,
    required this.endCursor,
  });

  final int pushed;
  final int pushFailed;
  final int conflicts;
  final int pulled;
  final int remainingOutbox;
  final bool stoppedByNetwork;
  final int startCursor;
  final int endCursor;
}

class AccountingSyncService {
  const AccountingSyncService({
    required this.localDatabase,
    required this.apiClient,
  });

  final LocalDatabase localDatabase;
  final ApiClient apiClient;

  Future<AccountingSyncRunResult> syncAll({
    required String companyId,
    required String bearerToken,
  }) async {
    final accounts = await apiClient.getAccounts(
      bearerToken: bearerToken,
    );
    final fiscalYears = await apiClient.getFiscalYears(
      bearerToken: bearerToken,
    );
    await localDatabase.replaceAccounts(
      companyId: companyId,
      accounts: accounts,
    );
    await localDatabase.replaceFiscalYears(
      companyId: companyId,
      fiscalYears: fiscalYears,
    );

    for (final fiscalYear in fiscalYears) {
      final fiscalYearId = fiscalYear['id'] as String;
      final periods = await apiClient.getFiscalPeriods(
        bearerToken: bearerToken,
        fiscalYearId: fiscalYearId,
      );

      await localDatabase.replaceFiscalPeriods(
        companyId: companyId,
        fiscalYearId: fiscalYearId,
        periods: periods,
      );
    }

    await localDatabase.backfillLegacyJournalFiscalYears(
      companyId,
    );

    final push = await OutboxSyncService(
      localDatabase: localDatabase,
      apiClient: apiClient,
    ).syncPending(
      bearerToken: bearerToken,
    );

    final currentCursor =
        await localDatabase.getJournalPullCursor(companyId);

    if (push.stoppedByNetwork) {
      return AccountingSyncRunResult(
        pushed: push.synced,
        pushFailed: push.failed,
        conflicts: push.conflicts,
        pulled: 0,
        remainingOutbox: push.remaining,
        stoppedByNetwork: true,
        startCursor: currentCursor,
        endCursor: currentCursor,
      );
    }

    try {
      await SalesInventoryCacheSyncService(
        localDatabase: localDatabase,
        apiClient: apiClient,
      ).refresh(
        companyId: companyId,
        bearerToken: bearerToken,
      );
    } catch (_) {
      // Store cache refresh is best-effort and must not block accounting sync.
    }

    await DetailAccountPullSyncService(
      localDatabase: localDatabase,
      apiClient: apiClient,
    ).pullAll(
      companyId: companyId,
      bearerToken: bearerToken,
    );

    final pull = await JournalPullSyncService(
      localDatabase: localDatabase,
      apiClient: apiClient,
    ).pullAll(
      companyId: companyId,
      bearerToken: bearerToken,
    );

    await localDatabase.setMeta(
      'last_accounting_sync_at',
      DateTime.now().toUtc().toIso8601String(),
    );

    return AccountingSyncRunResult(
      pushed: push.synced,
      pushFailed: push.failed,
      conflicts: push.conflicts,
      pulled: pull.pulled,
      remainingOutbox: push.remaining,
      stoppedByNetwork: false,
      startCursor: pull.startCursor,
      endCursor: pull.endCursor,
    );
  }
}

