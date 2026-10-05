import '../../core/database/local_database.dart';

class LocalDemoReportingEngine {
  const LocalDemoReportingEngine({
    required this.localDatabase,
  });

  final LocalDatabase localDatabase;

  Future<Map<String, dynamic>> getReportsCenter({
    required String companyId,
    DateTime? from,
    DateTime? to,
    String? warehouseId,
    String? productId,
    String? detailAccountId,
  }) async {
    if (from != null && to != null && from.isAfter(to)) {
      throw StateError('تاریخ شروع گزارش نمی‌تواند بعد از تاریخ پایان باشد.');
    }

    bool inRange(dynamic raw) {
      if (raw == null) return false;
      final value = DateTime.tryParse(raw.toString());
      if (value == null) return false;
      final day = DateTime(value.year, value.month, value.day);
      final start = from == null
          ? null
          : DateTime(from.year, from.month, from.day);
      final end =
          to == null ? null : DateTime(to.year, to.month, to.day);
      return (start == null || !day.isBefore(start)) &&
          (end == null || !day.isAfter(end));
    }

    num n(dynamic value) {
      if (value is num) return value;
      return num.tryParse(value?.toString() ?? '') ?? 0;
    }

    num rate(Map<String, dynamic> doc) {
      final value = n(doc['exchangeRate']);
      return value > 0 ? value : 1;
    }

    final products = await localDatabase.getCachedStoreEntities(
      companyId: companyId,
      entityType: 'StoreProduct',
    );
    final warehouses = await localDatabase.getCachedStoreEntities(
      companyId: companyId,
      entityType: 'Warehouse',
    );
    final invoices = await localDatabase.getCachedStoreEntities(
      companyId: companyId,
      entityType: 'SalesInvoice',
    );
    final salesReturns = await localDatabase.getCachedStoreEntities(
      companyId: companyId,
      entityType: 'SalesReturn',
    );
    final purchases = await localDatabase.getCachedStoreEntities(
      companyId: companyId,
      entityType: 'PurchaseReceipt',
    );
    final purchaseReturns =
        await localDatabase.getCachedStoreEntities(
      companyId: companyId,
      entityType: 'PurchaseReturn',
    );
    final treasuryAccounts =
        await localDatabase.getCachedStoreEntities(
      companyId: companyId,
      entityType: 'TreasuryAccount',
    );
    final treasuryTransactions =
        await localDatabase.getCachedStoreEntities(
      companyId: companyId,
      entityType: 'TreasuryTransaction',
    );
    final details =
        await localDatabase.getCachedDetailAccounts(companyId);
    final costCenters =
        await localDatabase.getCachedCostCenters(companyId);
    final projects =
        await localDatabase.getCachedAccountingProjects(companyId);

    final productMap = {
      for (final item in products)
        item['id'].toString(): item,
    };
    final warehouseMap = {
      for (final item in warehouses)
        item['id'].toString(): item,
    };
    final invoiceMap = {
      for (final item in invoices)
        item['id'].toString(): item,
    };
    final purchaseMap = {
      for (final item in purchases)
        item['id'].toString(): item,
    };

    final filteredInvoices = invoices.where((item) {
      return item['status']?.toString() != 'Draft' &&
          inRange(item['documentDate']) &&
          (warehouseId == null ||
              item['warehouseId']?.toString() == warehouseId) &&
          (detailAccountId == null ||
              item['customerDetailAccountId']?.toString() ==
                  detailAccountId);
    }).toList(growable: false);

    final filteredSalesReturns = salesReturns.where((item) {
      final original =
          invoiceMap[item['salesInvoiceId']?.toString()];
      return item['status']?.toString() == 'Posted' &&
          inRange(item['documentDate']) &&
          (warehouseId == null ||
              item['warehouseId']?.toString() == warehouseId) &&
          (detailAccountId == null ||
              original?['customerDetailAccountId']?.toString() ==
                  detailAccountId);
    }).toList(growable: false);

    final filteredPurchases = purchases.where((item) {
      return item['status']?.toString() != 'Draft' &&
          inRange(item['documentDate']) &&
          (warehouseId == null ||
              item['warehouseId']?.toString() == warehouseId) &&
          (detailAccountId == null ||
              item['supplierDetailAccountId']?.toString() ==
                  detailAccountId);
    }).toList(growable: false);

    final filteredPurchaseReturns =
        purchaseReturns.where((item) {
      final original =
          purchaseMap[item['purchaseReceiptId']?.toString()];
      return item['status']?.toString() == 'Posted' &&
          inRange(item['documentDate']) &&
          (warehouseId == null ||
              item['warehouseId']?.toString() == warehouseId) &&
          (detailAccountId == null ||
              original?['supplierDetailAccountId']?.toString() ==
                  detailAccountId);
    }).toList(growable: false);

    final productRows = <Map<String, dynamic>>[];

    for (final product in products) {
      if (productId != null &&
          product['id']?.toString() != productId) {
        continue;
      }

      final id = product['id'].toString();
      num sold = 0;
      num returned = 0;
      num sales = 0;
      num cogs = 0;
      num purchased = 0;
      num purchaseReturned = 0;
      num purchaseAmount = 0;

      for (final invoice in filteredInvoices) {
        for (final raw
            in (invoice['lines'] as List<dynamic>? ?? const [])) {
          final line = Map<String, dynamic>.from(raw as Map);
          if (line['productId']?.toString() != id) continue;
          sold += n(line['quantity']);
          sales += n(line['netAmount']) * rate(invoice);
          cogs += n(line['costAmount']);
        }
      }

      for (final ret in filteredSalesReturns) {
        final original =
            invoiceMap[ret['salesInvoiceId']?.toString()];
        final exchange = original == null ? 1 : rate(original);
        for (final raw
            in (ret['lines'] as List<dynamic>? ?? const [])) {
          final line = Map<String, dynamic>.from(raw as Map);
          if (line['productId']?.toString() != id) continue;
          returned += n(line['quantity']);
          sales -= n(line['netAmount']) * exchange;
          cogs -= n(line['costAmount']);
        }
      }

      for (final purchase in filteredPurchases) {
        for (final raw
            in (purchase['lines'] as List<dynamic>? ?? const [])) {
          final line = Map<String, dynamic>.from(raw as Map);
          if (line['productId']?.toString() != id) continue;
          purchased += n(line['quantity']);
          purchaseAmount +=
              n(line['netAmount']) * rate(purchase);
        }
      }

      for (final ret in filteredPurchaseReturns) {
        final original =
            purchaseMap[ret['purchaseReceiptId']?.toString()];
        final exchange = original == null ? 1 : rate(original);
        for (final raw
            in (ret['lines'] as List<dynamic>? ?? const [])) {
          final line = Map<String, dynamic>.from(raw as Map);
          if (line['productId']?.toString() != id) continue;
          purchaseReturned += n(line['quantity']);
          purchaseAmount -= n(line['netAmount']) * exchange;
        }
      }

      final grossProfit = sales - cogs;
      if (sold == 0 &&
          returned == 0 &&
          purchased == 0 &&
          purchaseReturned == 0 &&
          sales == 0 &&
          purchaseAmount == 0) {
        continue;
      }

      productRows.add({
        'productId': id,
        'sku': product['sku'],
        'productName': product['name'],
        'soldQuantity': sold,
        'returnedQuantity': returned,
        'netSoldQuantity': sold - returned,
        'netSales': sales,
        'costOfGoodsSold': cogs,
        'grossProfit': grossProfit,
        'grossMarginPercent':
            sales == 0 ? 0 : grossProfit / sales * 100,
        'purchasedQuantity': purchased,
        'purchaseReturnedQuantity': purchaseReturned,
        'netPurchasedQuantity': purchased - purchaseReturned,
        'netPurchases': purchaseAmount,
      });
    }

    productRows.sort(
      (a, b) => n(b['netSales']).compareTo(n(a['netSales'])),
    );

    final stockRows =
        await localDatabase.runDemoBusinessTransaction((txn) {
      return txn.query(
        'demo_stock_movements',
        where: 'company_id = ?',
        whereArgs: [companyId],
      );
    });

    final stockGroups = <String, List<Map<String, Object?>>>{};
    for (final row in stockRows) {
      final whId = row['warehouse_id']?.toString();
      final pId = row['product_id']?.toString();
      if (whId == null || pId == null) continue;
      if (warehouseId != null && whId != warehouseId) continue;
      if (productId != null && pId != productId) continue;
      final key = '$whId|$pId';
      stockGroups.putIfAbsent(key, () => []).add(row);
    }

    final inventoryRows = <Map<String, dynamic>>[];
    num inventoryValue = 0;

    for (final entry in stockGroups.entries) {
      final parts = entry.key.split('|');
      final wh = warehouseMap[parts[0]];
      final product = productMap[parts[1]];
      if (wh == null || product == null) continue;

      num opening = 0;
      num incoming = 0;
      num outgoing = 0;
      num closing = 0;
      num value = 0;

      for (final row in entry.value) {
        final date = DateTime.tryParse(
          row['document_date']?.toString() ?? '',
        );
        if (date == null) continue;

        final quantity = n(row['quantity']);
        final unitCost = n(row['unit_cost']);
        final day = DateTime(date.year, date.month, date.day);
        final start = from == null
            ? null
            : DateTime(from.year, from.month, from.day);
        final end =
            to == null ? null : DateTime(to.year, to.month, to.day);

        if (end != null && day.isAfter(end)) continue;

        closing += quantity;
        value += quantity * unitCost;

        if (start != null && day.isBefore(start)) {
          opening += quantity;
          continue;
        }

        if (quantity > 0) {
          incoming += quantity;
        } else {
          outgoing += -quantity;
        }
      }

      inventoryValue += value;
      inventoryRows.add({
        'warehouseId': parts[0],
        'warehouseCode': wh['code'],
        'warehouseName': wh['name'],
        'productId': parts[1],
        'sku': product['sku'],
        'productName': product['name'],
        'openingQuantity': opening,
        'inQuantity': incoming,
        'outQuantity': outgoing,
        'closingQuantity': closing,
        'closingValue': value,
      });
    }

    final treasuryRows = <Map<String, dynamic>>[];
    num treasuryReceiptTotal = 0;
    num treasuryPaymentTotal = 0;

    for (final account in treasuryAccounts) {
      final id = account['id']?.toString();
      if (id == null) continue;

      num received = 0;
      num paid = 0;
      num transferIn = 0;
      num transferOut = 0;

      for (final tx in treasuryTransactions) {
        if (tx['status']?.toString() != 'Posted' ||
            !inRange(tx['documentDate']) ||
            (detailAccountId != null &&
                tx['detailAccountId']?.toString() !=
                    detailAccountId)) {
          continue;
        }

        final amount = n(tx['amount']) * rate(tx);
        switch (tx['type']?.toString()) {
          case 'Receipt':
            if (tx['toTreasuryAccountId']?.toString() == id) {
              received += amount;
              treasuryReceiptTotal += amount;
            }
            break;
          case 'Payment':
            if (tx['fromTreasuryAccountId']?.toString() == id) {
              paid += amount;
              treasuryPaymentTotal += amount;
            }
            break;
          case 'Transfer':
            if (tx['toTreasuryAccountId']?.toString() == id) {
              transferIn += amount;
            }
            if (tx['fromTreasuryAccountId']?.toString() == id) {
              transferOut += amount;
            }
            break;
        }
      }

      treasuryRows.add({
        'treasuryAccountId': id,
        'code': account['code'],
        'name': account['name'],
        'type': account['type'],
        'receipts': received,
        'payments': paid,
        'transfersIn': transferIn,
        'transfersOut': transferOut,
        'netFlow': received - paid + transferIn - transferOut,
      });
    }

    final partyRows = <Map<String, dynamic>>[];
    for (final detail in details) {
      if (detailAccountId != null &&
          detail.id != detailAccountId) {
        continue;
      }

      num sales = 0;
      num purchasesAmount = 0;
      num received = 0;
      num paid = 0;

      for (final invoice in filteredInvoices) {
        if (invoice['customerDetailAccountId']?.toString() ==
            detail.id) {
          sales += n(invoice['grandTotal']) * rate(invoice);
        }
      }

      for (final ret in filteredSalesReturns) {
        final original =
            invoiceMap[ret['salesInvoiceId']?.toString()];
        if (original != null &&
            original['customerDetailAccountId']?.toString() ==
                detail.id) {
          sales -= n(ret['grandTotal']) * rate(original);
        }
      }

      for (final purchase in filteredPurchases) {
        if (purchase['supplierDetailAccountId']?.toString() ==
            detail.id) {
          purchasesAmount +=
              n(purchase['grandTotal']) * rate(purchase);
        }
      }

      for (final ret in filteredPurchaseReturns) {
        final original =
            purchaseMap[ret['purchaseReceiptId']?.toString()];
        if (original != null &&
            original['supplierDetailAccountId']?.toString() ==
                detail.id) {
          purchasesAmount -=
              n(ret['grandTotal']) * rate(original);
        }
      }

      for (final tx in treasuryTransactions) {
        if (tx['status']?.toString() != 'Posted' ||
            !inRange(tx['documentDate']) ||
            tx['detailAccountId']?.toString() != detail.id) {
          continue;
        }

        final amount = n(tx['amount']) * rate(tx);
        if (tx['type']?.toString() == 'Receipt') {
          received += amount;
        } else if (tx['type']?.toString() == 'Payment') {
          paid += amount;
        }
      }

      if (sales == 0 &&
          purchasesAmount == 0 &&
          received == 0 &&
          paid == 0) {
        continue;
      }

      final exposure = detail.type == 'Customer'
          ? sales - received
          : detail.type == 'Supplier'
              ? purchasesAmount - paid
              : sales - received - purchasesAmount + paid;

      partyRows.add({
        'detailAccountId': detail.id,
        'code': detail.code,
        'name': detail.name,
        'type': detail.type,
        'sales': sales,
        'purchases': purchasesAmount,
        'receipts': received,
        'payments': paid,
        'netCommercialFlow': exposure,
      });
    }

    final dimensionRows =
        await localDatabase.runDemoBusinessTransaction((txn) {
      return txn.rawQuery(
        '''
        SELECT
          d.document_date,
          l.debit,
          l.credit,
          l.detail_account_id,
          l.cost_center_id,
          l.project_id
        FROM local_document_lines l
        INNER JOIN local_accounting_documents d
          ON d.id = l.document_id
        WHERE d.company_id = ?
          AND d.status != 'Draft'
        ''',
        [companyId],
      );
    });

    final centerTotals = <String, List<num>>{};
    final projectTotals = <String, List<num>>{};

    for (final row in dimensionRows) {
      if (!inRange(row['document_date'])) continue;
      if (detailAccountId != null &&
          row['detail_account_id']?.toString() != detailAccountId) {
        continue;
      }
      final debit = n(row['debit']);
      final credit = n(row['credit']);
      final center = row['cost_center_id']?.toString();
      final project = row['project_id']?.toString();

      if (center != null && center.isNotEmpty) {
        final totals =
            centerTotals.putIfAbsent(center, () => [0, 0]);
        totals[0] += debit;
        totals[1] += credit;
      }

      if (project != null && project.isNotEmpty) {
        final totals =
            projectTotals.putIfAbsent(project, () => [0, 0]);
        totals[0] += debit;
        totals[1] += credit;
      }
    }

    final costCenterRows = [
      for (final center in costCenters)
        if (centerTotals.containsKey(center.id))
          {
            'dimensionId': center.id,
            'code': center.code,
            'name': center.name,
            'dimensionType': 'CostCenter',
            'debit': centerTotals[center.id]![0],
            'credit': centerTotals[center.id]![1],
            'net': centerTotals[center.id]![0] -
                centerTotals[center.id]![1],
          },
    ];

    final projectRows = [
      for (final project in projects)
        if (projectTotals.containsKey(project.id))
          {
            'dimensionId': project.id,
            'code': project.code,
            'name': project.name,
            'dimensionType': 'Project',
            'debit': projectTotals[project.id]![0],
            'credit': projectTotals[project.id]![1],
            'net': projectTotals[project.id]![0] -
                projectTotals[project.id]![1],
          },
    ];

    final netSales = productRows.fold<num>(
      0,
      (sum, item) => sum + n(item['netSales']),
    );
    final cogs = productRows.fold<num>(
      0,
      (sum, item) => sum + n(item['costOfGoodsSold']),
    );
    final netPurchases = productRows.fold<num>(
      0,
      (sum, item) => sum + n(item['netPurchases']),
    );

    return {
      'from': from?.toIso8601String().split('T').first,
      'to': to?.toIso8601String().split('T').first,
      'summary': {
        'netSales': netSales,
        'costOfGoodsSold': cogs,
        'grossProfit': netSales - cogs,
        'netPurchases': netPurchases,
        'treasuryNetFlow':
            treasuryReceiptTotal - treasuryPaymentTotal,
        'inventoryValue': inventoryValue,
      },
      'products': productRows,
      'parties': partyRows,
      'inventory': inventoryRows,
      'treasury': treasuryRows,
      'costCenters': costCenterRows,
      'projects': projectRows,
    };
  }
}
