import '../api/api_client.dart';
import '../database/local_database.dart';

class SalesInventoryCacheSyncService {
  const SalesInventoryCacheSyncService({
    required this.localDatabase,
    required this.apiClient,
  });

  final LocalDatabase localDatabase;
  final ApiClient apiClient;

  Future<void> refresh({
    required String companyId,
    required String bearerToken,
  }) async {
    await apiClient.ensureSalesInventoryDefaults(
      bearerToken: bearerToken,
    );

    final results = await Future.wait([
      apiClient.getStoreProducts(
        bearerToken: bearerToken,
      ),
      apiClient.getWarehouses(
        bearerToken: bearerToken,
      ),
      apiClient.getStockBalances(
        bearerToken: bearerToken,
      ),
      apiClient.getSalesInvoices(
        bearerToken: bearerToken,
      ),
      apiClient.getPurchaseReceipts(
        bearerToken: bearerToken,
      ),
      apiClient.getWarehouseTransfers(
        bearerToken: bearerToken,
      ),
      apiClient.getSalesReturns(
        bearerToken: bearerToken,
      ),
      apiClient.getLowStockAlerts(
        bearerToken: bearerToken,
      ),
      apiClient.getStockTraceBalances(
        bearerToken: bearerToken,
      ),
    ]);

    const entityTypes = [
      'StoreProduct',
      'Warehouse',
      'StockBalance',
      'SalesInvoice',
      'PurchaseReceipt',
      'WarehouseTransfer',
      'SalesReturn',
      'LowStockAlert',
      'StockTrace',
    ];

    for (var index = 0; index < entityTypes.length; index++) {
      await localDatabase.replaceStoreEntities(
        companyId: companyId,
        entityType: entityTypes[index],
        items: results[index],
      );
    }

    await localDatabase.setMeta(
      'last_store_sync_at',
      DateTime.now().toUtc().toIso8601String(),
    );
  }
}
