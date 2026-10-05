import 'dart:convert';
import 'dart:math';

import 'package:sqflite_common/sqlite_api.dart';

import '../database/local_database.dart';
import 'demo_mode.dart';

class LocalDemoBusinessEngine {
  LocalDemoBusinessEngine({
    required this.localDatabase,
  });

  final LocalDatabase localDatabase;

  static const _cashAccountId =
      '00000000-0000-4000-8000-000000000601';
  static const _receivablesAccountId =
      '00000000-0000-4000-8000-000000000603';
  static const _inventoryAccountId =
      '00000000-0000-4000-8000-000000000604';
  static const _payablesAccountId =
      '00000000-0000-4000-8000-000000000605';
  static const _salesTaxAccountId =
      '00000000-0000-4000-8000-000000000606';
  static const _salesRevenueAccountId =
      '00000000-0000-4000-8000-000000000608';
  static const _cogsAccountId =
      '00000000-0000-4000-8000-000000000609';
  static const _purchaseTaxAccountId =
      '00000000-0000-4000-8000-000000000611';

  final Random _random = Random.secure();

  Future<void> initialize() async {
    await localDatabase.runDemoBusinessTransaction((txn) async {
      final countRows = await txn.rawQuery(
        'SELECT COUNT(*) AS count FROM demo_stock_movements '
        'WHERE company_id = ?',
        [DemoMode.companyId],
      );
      final count = (countRows.first['count'] as num?)?.toInt() ?? 0;

      if (count == 0) {
        final products = await _readCachedMap(
          txn,
          'StoreProduct',
        );
        final balances = await _readCachedList(
          txn,
          'StockBalance',
        );

        for (final balance in balances) {
          final quantity = _num(balance['quantity']);
          if (quantity <= 0) continue;

          final productId = balance['productId']?.toString();
          final warehouseId = balance['warehouseId']?.toString();
          if (productId == null || warehouseId == null) continue;

          final product = products[productId];
          final trackingMode =
              product?['trackingMode']?.toString() ?? 'None';

          await _insertMovement(
            txn,
            warehouseId: warehouseId,
            productId: productId,
            documentDate: '2026-03-21',
            movementType: 'Opening',
            quantity: quantity,
            unitCost: _num(balance['averageCost']),
            lotNumber: trackingMode == 'Lot'
                ? 'DEMO-LOT-001'
                : null,
            expiryDate: trackingMode == 'Lot'
                ? '2027-12-31'
                : null,
            referenceType: 'DemoOpening',
            description: 'موجودی افتتاحیه حالت تست',
          );
        }
      }

      await _refreshStockCaches(txn);
    });
  }

  Future<Map<String, dynamic>> postSalesInvoice(
    String draftId,
  ) {
    return localDatabase.runDemoBusinessTransaction((txn) async {
      final payload = await _readDraft(
        txn,
        draftId,
        'StoreSalesInvoiceDraft',
      );

      final products = await _readCachedMap(
        txn,
        'StoreProduct',
      );
      final warehouses = await _readCachedMap(
        txn,
        'Warehouse',
      );

      final warehouseId = payload['warehouseId']?.toString();
      final fiscalYearId = payload['fiscalYearId']?.toString();

      if (warehouseId == null ||
          !warehouses.containsKey(warehouseId)) {
        throw StateError('انبار فاکتور در داده محلی پیدا نشد.');
      }

      if (fiscalYearId == null || fiscalYearId.isEmpty) {
        throw StateError('سال مالی فاکتور مشخص نیست.');
      }

      final rawLines =
          payload['lines'] as List<dynamic>? ?? const [];
      if (rawLines.isEmpty) {
        throw StateError('فاکتور فروش فاقد ردیف است.');
      }

      final number = await _nextCachedNumber(
        txn,
        entityType: 'SalesInvoice',
        prefix: 'DEMO-S-',
      );
      final documentDate =
          payload['documentDate']?.toString() ?? _dateOnly(DateTime.now());
      final paymentType =
          payload['paymentType']?.toString() ?? 'Cash';

      if (paymentType == 'Credit' &&
          payload['customerDetailAccountId'] == null) {
        throw StateError('برای فروش نسیه مشتری الزامی است.');
      }

      num subtotal = 0;
      num discountTotal = 0;
      num taxTotal = 0;
      num totalCost = 0;
      final postedLines = <Map<String, dynamic>>[];

      for (final raw in rawLines) {
        final line = Map<String, dynamic>.from(raw as Map);
        final productId = line['productId']?.toString();
        final product = productId == null ? null : products[productId];

        if (productId == null || product == null) {
          throw StateError('یکی از کالاهای فاکتور در کش محلی وجود ندارد.');
        }

        final quantity = _num(line['quantity']);
        final unitPrice = line['unitPrice'] == null
            ? _num(product['salesPrice'])
            : _num(line['unitPrice']);
        final discount = _num(line['discountAmount']);
        final tax = _num(line['taxAmount']);

        if (quantity <= 0 || unitPrice < 0 ||
            discount < 0 || tax < 0) {
          throw StateError('مقادیر یکی از ردیف‌های فروش معتبر نیست.');
        }

        final gross = quantity * unitPrice;
        if (discount > gross) {
          throw StateError('تخفیف یک ردیف از مبلغ ناخالص بیشتر است.');
        }

        final net = gross - discount;
        num unitCost = 0;
        num costAmount = 0;

        final trackInventory =
            product['trackInventory'] as bool? ?? false;

        if (trackInventory) {
          _validateTrace(product, line, quantity);

          final balance = await _traceBalance(
            txn,
            warehouseId: warehouseId,
            productId: productId,
            trackingMode:
                product['trackingMode']?.toString() ?? 'None',
            lotNumber: _text(line['lotNumber']),
            serialNumber: _text(line['serialNumber']),
            expiryDate: _text(line['expiryDate']),
          );

          if (balance.quantity < quantity) {
            throw StateError(
              'موجودی «' +
                  product['name'].toString() +
                  '» کافی نیست. موجودی: ' +
                  _plain(balance.quantity) +
                  '، درخواست: ' +
                  _plain(quantity),
            );
          }

          unitCost = balance.quantity == 0
              ? _num(product['defaultPurchasePrice'])
              : balance.value / balance.quantity;
          costAmount = unitCost * quantity;

          await _insertMovement(
            txn,
            warehouseId: warehouseId,
            productId: productId,
            documentDate: documentDate,
            movementType: 'SaleIssue',
            quantity: -quantity,
            unitCost: unitCost,
            lotNumber: _text(line['lotNumber']),
            serialNumber: _text(line['serialNumber']),
            expiryDate: _text(line['expiryDate']),
            referenceType: 'SalesInvoice',
            referenceId: draftId,
            description: 'خروج بابت فاکتور فروش $number',
          );
        }

        subtotal += gross;
        discountTotal += discount;
        taxTotal += tax;
        totalCost += costAmount;

        postedLines.add({
          ...line,
          'id': _newId(),
          'sku': product['sku'],
          'productName': product['name'],
          'unitName': product['unitName'] ?? 'عدد',
          'unitPrice': unitPrice,
          'netAmount': net,
          'unitCost': unitCost,
          'costAmount': costAmount,
        });
      }

      final grandTotal =
          subtotal - discountTotal + taxTotal;
      if (grandTotal <= 0) {
        throw StateError('مبلغ نهایی فاکتور باید بزرگ‌تر از صفر باشد.');
      }

      final settlementAmount = _money(grandTotal);
      final taxAmount = _money(taxTotal);
      final revenueAmount = settlementAmount - taxAmount;
      final costAmount = _money(totalCost);

      final journalLines = <_DemoJournalLine>[
        _DemoJournalLine(
          accountId: paymentType == 'Cash'
              ? _cashAccountId
              : _receivablesAccountId,
          detailAccountId: paymentType == 'Credit'
              ? payload['customerDetailAccountId']?.toString()
              : null,
          description: 'فاکتور فروش $number',
          debit: settlementAmount,
        ),
        if (revenueAmount > 0)
          _DemoJournalLine(
            accountId: _salesRevenueAccountId,
            description: 'درآمد فروش فاکتور $number',
            credit: revenueAmount,
          ),
        if (taxAmount > 0)
          _DemoJournalLine(
            accountId: _salesTaxAccountId,
            description: 'مالیات و عوارض فاکتور $number',
            credit: taxAmount,
          ),
        if (costAmount > 0)
          _DemoJournalLine(
            accountId: _cogsAccountId,
            description: 'بهای تمام‌شده فاکتور $number',
            debit: costAmount,
          ),
        if (costAmount > 0)
          _DemoJournalLine(
            accountId: _inventoryAccountId,
            description: 'خروج موجودی فاکتور $number',
            credit: costAmount,
          ),
      ];

      final journalNumber = await _insertPostedJournal(
        txn,
        fiscalYearId: fiscalYearId,
        documentDate: documentDate,
        description: 'ثبت حسابداری فاکتور فروش $number',
        lines: journalLines,
      );

      final warehouse = warehouses[warehouseId]!;
      final customerName = await _detailAccountName(
        txn,
        payload['customerDetailAccountId']?.toString(),
      );

      final posted = <String, dynamic>{
        'id': draftId,
        'number': number,
        'documentDate': documentDate,
        'warehouseId': warehouseId,
        'warehouseName': warehouse['name'],
        'customerDetailAccountId':
            payload['customerDetailAccountId'],
        'customerName': customerName,
        'paymentType': paymentType,
        'currencyCode': 'BASE',
        'exchangeRate': 1,
        'status': 'Posted',
        'description': payload['description'],
        'subtotal': subtotal,
        'discountTotal': discountTotal,
        'taxTotal': taxTotal,
        'grandTotal': grandTotal,
        'costTotal': totalCost,
        'accountingJournalNumber': journalNumber,
        'postedAt': DateTime.now().toUtc().toIso8601String(),
        'lines': postedLines,
      };

      await _upsertCachedEntity(
        txn,
        'SalesInvoice',
        draftId,
        posted,
      );
      await _finishDraft(txn, draftId);
      await _refreshStockCaches(txn);

      return {
        'id': draftId,
        'number': number,
        'accountingJournalNumber': journalNumber,
        'grandTotal': grandTotal,
        'costTotal': totalCost,
      };
    });
  }

  Future<Map<String, dynamic>> postPurchaseReceipt(
    String draftId,
  ) {
    return localDatabase.runDemoBusinessTransaction((txn) async {
      final payload = await _readDraft(
        txn,
        draftId,
        'StorePurchaseReceiptDraft',
      );

      final products = await _readCachedMap(
        txn,
        'StoreProduct',
      );
      final warehouses = await _readCachedMap(
        txn,
        'Warehouse',
      );

      final warehouseId = payload['warehouseId']?.toString();
      final fiscalYearId = payload['fiscalYearId']?.toString();

      if (warehouseId == null ||
          !warehouses.containsKey(warehouseId)) {
        throw StateError('انبار خرید در داده محلی پیدا نشد.');
      }
      if (fiscalYearId == null || fiscalYearId.isEmpty) {
        throw StateError('سال مالی خرید مشخص نیست.');
      }

      final paymentType =
          payload['paymentType']?.toString() ?? 'Credit';
      if (paymentType == 'Credit' &&
          payload['supplierDetailAccountId'] == null) {
        throw StateError('برای خرید نسیه تامین‌کننده الزامی است.');
      }

      final rawLines =
          payload['lines'] as List<dynamic>? ?? const [];
      if (rawLines.isEmpty) {
        throw StateError('رسید خرید فاقد ردیف است.');
      }

      final number = await _nextCachedNumber(
        txn,
        entityType: 'PurchaseReceipt',
        prefix: 'DEMO-P-',
      );
      final documentDate =
          payload['documentDate']?.toString() ?? _dateOnly(DateTime.now());

      num subtotal = 0;
      num discountTotal = 0;
      num taxTotal = 0;
      final postedLines = <Map<String, dynamic>>[];

      for (final raw in rawLines) {
        final line = Map<String, dynamic>.from(raw as Map);
        final productId = line['productId']?.toString();
        final product = productId == null ? null : products[productId];

        if (productId == null || product == null) {
          throw StateError('یکی از کالاهای خرید در کش محلی وجود ندارد.');
        }

        if (!(product['trackInventory'] as bool? ?? false)) {
          throw StateError(
            'رسید خرید دمو فقط برای کالاهای موجودی‌دار قابل ثبت است.',
          );
        }

        final quantity = _num(line['quantity']);
        final unitCost = line['unitCost'] == null
            ? _num(product['defaultPurchasePrice'])
            : _num(line['unitCost']);
        final discount = _num(line['discountAmount']);
        final tax = _num(line['taxAmount']);

        if (quantity <= 0 || unitCost < 0 ||
            discount < 0 || tax < 0) {
          throw StateError('مقادیر یکی از ردیف‌های خرید معتبر نیست.');
        }

        _validateTrace(product, line, quantity);

        final gross = quantity * unitCost;
        if (discount > gross) {
          throw StateError('تخفیف خرید از مبلغ ناخالص بیشتر است.');
        }

        final net = gross - discount;
        final inventoryUnitCost = net / quantity;

        if ((product['trackingMode']?.toString() ?? 'None') ==
                'Serial' &&
            _text(line['serialNumber']) != null) {
          final existing = await _serialQuantity(
            txn,
            productId,
            _text(line['serialNumber'])!,
          );
          if (existing > 0) {
            throw StateError(
              'سریال «' +
                  line['serialNumber'].toString() +
                  '» قبلاً در موجودی ثبت شده است.',
            );
          }
        }

        await _insertMovement(
          txn,
          warehouseId: warehouseId,
          productId: productId,
          documentDate: documentDate,
          movementType: 'PurchaseReceipt',
          quantity: quantity,
          unitCost: inventoryUnitCost,
          lotNumber: _text(line['lotNumber']),
          serialNumber: _text(line['serialNumber']),
          expiryDate: _text(line['expiryDate']),
          referenceType: 'PurchaseReceipt',
          referenceId: draftId,
          description: 'ورود خرید $number',
        );

        product['defaultPurchasePrice'] = inventoryUnitCost;
        await _upsertCachedEntity(
          txn,
          'StoreProduct',
          productId,
          product,
        );

        subtotal += gross;
        discountTotal += discount;
        taxTotal += tax;

        postedLines.add({
          ...line,
          'id': _newId(),
          'sku': product['sku'],
          'productName': product['name'],
          'unitName': product['unitName'] ?? 'عدد',
          'unitCost': unitCost,
          'netAmount': net,
        });
      }

      final grandTotal =
          subtotal - discountTotal + taxTotal;
      if (grandTotal <= 0) {
        throw StateError('مبلغ نهایی خرید باید بزرگ‌تر از صفر باشد.');
      }

      final settlementAmount = _money(grandTotal);
      final taxAmount = _money(taxTotal);
      final inventoryAmount = settlementAmount - taxAmount;

      final journalNumber = await _insertPostedJournal(
        txn,
        fiscalYearId: fiscalYearId,
        documentDate: documentDate,
        description: 'ثبت حسابداری خرید $number',
        lines: [
          _DemoJournalLine(
            accountId: _inventoryAccountId,
            description: 'خرید کالا $number',
            debit: inventoryAmount,
          ),
          if (taxAmount > 0)
            _DemoJournalLine(
              accountId: _purchaseTaxAccountId,
              description: 'مالیات خرید $number',
              debit: taxAmount,
            ),
          _DemoJournalLine(
            accountId: paymentType == 'Cash'
                ? _cashAccountId
                : _payablesAccountId,
            detailAccountId: paymentType == 'Credit'
                ? payload['supplierDetailAccountId']?.toString()
                : null,
            description: 'تسویه خرید $number',
            credit: settlementAmount,
          ),
        ],
      );

      final warehouse = warehouses[warehouseId]!;
      final supplierName = await _detailAccountName(
        txn,
        payload['supplierDetailAccountId']?.toString(),
      );

      final posted = <String, dynamic>{
        'id': draftId,
        'number': number,
        'documentDate': documentDate,
        'warehouseId': warehouseId,
        'warehouseName': warehouse['name'],
        'supplierDetailAccountId':
            payload['supplierDetailAccountId'],
        'supplierName': supplierName,
        'paymentType': paymentType,
        'currencyCode': 'BASE',
        'exchangeRate': 1,
        'status': 'Posted',
        'description': payload['description'],
        'subtotal': subtotal,
        'discountTotal': discountTotal,
        'taxTotal': taxTotal,
        'grandTotal': grandTotal,
        'accountingJournalNumber': journalNumber,
        'postedAt': DateTime.now().toUtc().toIso8601String(),
        'lines': postedLines,
      };

      await _upsertCachedEntity(
        txn,
        'PurchaseReceipt',
        draftId,
        posted,
      );
      await _finishDraft(txn, draftId);
      await _refreshStockCaches(txn);

      return {
        'id': draftId,
        'number': number,
        'accountingJournalNumber': journalNumber,
        'grandTotal': grandTotal,
      };
    });
  }

  Future<Map<String, dynamic>> postWarehouseTransfer(
    String draftId,
  ) {
    return localDatabase.runDemoBusinessTransaction((txn) async {
      final payload = await _readDraft(
        txn,
        draftId,
        'StoreWarehouseTransferDraft',
      );

      final products = await _readCachedMap(
        txn,
        'StoreProduct',
      );
      final warehouses = await _readCachedMap(
        txn,
        'Warehouse',
      );

      final fromWarehouseId =
          payload['fromWarehouseId']?.toString();
      final toWarehouseId =
          payload['toWarehouseId']?.toString();

      if (fromWarehouseId == null ||
          toWarehouseId == null ||
          fromWarehouseId == toWarehouseId ||
          !warehouses.containsKey(fromWarehouseId) ||
          !warehouses.containsKey(toWarehouseId)) {
        throw StateError('انبار مبدا یا مقصد انتقال معتبر نیست.');
      }

      final rawLines =
          payload['lines'] as List<dynamic>? ?? const [];
      if (rawLines.isEmpty) {
        throw StateError('انتقال انبار فاقد ردیف است.');
      }

      final number = await _nextCachedNumber(
        txn,
        entityType: 'WarehouseTransfer',
        prefix: 'DEMO-T-',
      );
      final documentDate =
          payload['documentDate']?.toString() ?? _dateOnly(DateTime.now());
      final postedLines = <Map<String, dynamic>>[];

      for (final raw in rawLines) {
        final line = Map<String, dynamic>.from(raw as Map);
        final productId = line['productId']?.toString();
        final product = productId == null ? null : products[productId];

        if (productId == null || product == null ||
            !(product['trackInventory'] as bool? ?? false)) {
          throw StateError('کالای انتقال معتبر نیست.');
        }

        final quantity = _num(line['quantity']);
        if (quantity <= 0) {
          throw StateError('تعداد انتقال باید بزرگ‌تر از صفر باشد.');
        }

        _validateTrace(product, line, quantity);

        final balance = await _traceBalance(
          txn,
          warehouseId: fromWarehouseId,
          productId: productId,
          trackingMode:
              product['trackingMode']?.toString() ?? 'None',
          lotNumber: _text(line['lotNumber']),
          serialNumber: _text(line['serialNumber']),
          expiryDate: _text(line['expiryDate']),
        );

        if (balance.quantity < quantity) {
          throw StateError(
            'موجودی «' +
                product['name'].toString() +
                '» برای انتقال کافی نیست.',
          );
        }

        final averageCost = balance.quantity == 0
            ? _num(product['defaultPurchasePrice'])
            : balance.value / balance.quantity;

        await _insertMovement(
          txn,
          warehouseId: fromWarehouseId,
          productId: productId,
          documentDate: documentDate,
          movementType: 'TransferOut',
          quantity: -quantity,
          unitCost: averageCost,
          lotNumber: _text(line['lotNumber']),
          serialNumber: _text(line['serialNumber']),
          expiryDate: _text(line['expiryDate']),
          referenceType: 'WarehouseTransfer',
          referenceId: draftId,
          description: 'خروج انتقال $number',
        );

        await _insertMovement(
          txn,
          warehouseId: toWarehouseId,
          productId: productId,
          documentDate: documentDate,
          movementType: 'TransferIn',
          quantity: quantity,
          unitCost: averageCost,
          lotNumber: _text(line['lotNumber']),
          serialNumber: _text(line['serialNumber']),
          expiryDate: _text(line['expiryDate']),
          referenceType: 'WarehouseTransfer',
          referenceId: draftId,
          description: 'ورود انتقال $number',
        );

        postedLines.add({
          ...line,
          'id': _newId(),
          'sku': product['sku'],
          'productName': product['name'],
          'unitName': product['unitName'] ?? 'عدد',
        });
      }

      final posted = <String, dynamic>{
        'id': draftId,
        'number': number,
        'documentDate': documentDate,
        'fromWarehouseId': fromWarehouseId,
        'fromWarehouseName': warehouses[fromWarehouseId]!['name'],
        'toWarehouseId': toWarehouseId,
        'toWarehouseName': warehouses[toWarehouseId]!['name'],
        'description': payload['description'],
        'status': 'Posted',
        'postedAt': DateTime.now().toUtc().toIso8601String(),
        'lines': postedLines,
      };

      await _upsertCachedEntity(
        txn,
        'WarehouseTransfer',
        draftId,
        posted,
      );
      await _finishDraft(txn, draftId);
      await _refreshStockCaches(txn);

      return posted;
    });
  }

  Future<Map<String, dynamic>> createSalesReturn({
    required String invoiceId,
    required DateTime documentDate,
    required String reason,
    required List<Map<String, dynamic>> lines,
  }) {
    return localDatabase.runDemoBusinessTransaction((txn) async {
      await _ensureCapacityForNewDocument(txn);

      final trimmedReason = reason.trim();
      if (trimmedReason.isEmpty) {
        throw StateError('علت برگشت از فروش الزامی است.');
      }
      if (lines.isEmpty) {
        throw StateError('حداقل یک ردیف برای برگشت لازم است.');
      }

      final invoiceRows = await txn.query(
        'cached_store_entities',
        columns: ['payload_json'],
        where: 'company_id = ? AND entity_type = ? AND entity_id = ?',
        whereArgs: [
          DemoMode.companyId,
          'SalesInvoice',
          invoiceId,
        ],
        limit: 1,
      );

      if (invoiceRows.isEmpty) {
        throw StateError('فاکتور فروش قطعی در داده محلی پیدا نشد.');
      }

      final invoice = Map<String, dynamic>.from(
        jsonDecode(invoiceRows.first['payload_json'] as String) as Map,
      );

      final invoiceStatus = invoice['status']?.toString();
      if (invoiceStatus != 'Posted' && invoiceStatus != 'Reversed') {
        throw StateError('فقط فاکتور قطعی قابل برگشت است.');
      }

      final originalLines = <String, Map<String, dynamic>>{
        for (final raw in
            (invoice['lines'] as List<dynamic>? ?? const []))
          Map<String, dynamic>.from(raw as Map)['id'].toString():
              Map<String, dynamic>.from(raw as Map),
      };

      final previousReturns = await _readCachedList(
        txn,
        'SalesReturn',
      );
      final returnedByLine = <String, num>{};

      for (final salesReturn in previousReturns) {
        if (salesReturn['salesInvoiceId']?.toString() != invoiceId ||
            salesReturn['status']?.toString() != 'Posted') {
          continue;
        }

        for (final raw in
            (salesReturn['lines'] as List<dynamic>? ?? const [])) {
          final line = Map<String, dynamic>.from(raw as Map);
          final lineId = line['salesInvoiceLineId']?.toString();
          if (lineId == null) continue;
          returnedByLine[lineId] =
              (returnedByLine[lineId] ?? 0) + _num(line['quantity']);
        }
      }

      final products = await _readCachedMap(
        txn,
        'StoreProduct',
      );
      final warehouses = await _readCachedMap(
        txn,
        'Warehouse',
      );
      final warehouseId = invoice['warehouseId']?.toString();

      if (warehouseId == null || !warehouses.containsKey(warehouseId)) {
        throw StateError('انبار فاکتور برگشتی پیدا نشد.');
      }

      final number = await _nextCachedNumber(
        txn,
        entityType: 'SalesReturn',
        prefix: 'DEMO-R-',
      );
      final returnId = _newId();
      final postedLines = <Map<String, dynamic>>[];
      final newReturnedByLine = <String, num>{};

      num grandTotal = 0;
      num taxTotal = 0;
      num costTotal = 0;

      for (final requested in lines) {
        final lineId = requested['salesInvoiceLineId']?.toString();
        final quantity = _num(requested['quantity']);
        final original = lineId == null ? null : originalLines[lineId];

        if (lineId == null || original == null) {
          throw StateError('یکی از ردیف‌های برگشت متعلق به فاکتور نیست.');
        }
        if (quantity <= 0) {
          throw StateError('تعداد برگشت باید بزرگ‌تر از صفر باشد.');
        }

        final originalQuantity = _num(original['quantity']);
        final alreadyReturned = returnedByLine[lineId] ?? 0;
        if (alreadyReturned + quantity > originalQuantity) {
          throw StateError(
            'تعداد برگشت از مانده قابل برگشت ردیف بیشتر است.',
          );
        }

        final ratio = quantity / originalQuantity;
        final netAmount = _num(original['netAmount']) * ratio;
        final taxAmount = _num(original['taxAmount']) * ratio;
        final costAmount = _num(original['costAmount']) * ratio;
        final unitCost = _num(original['unitCost']);
        final productId = original['productId']?.toString();
        final product = productId == null ? null : products[productId];

        if (productId == null || product == null) {
          throw StateError('کالای ردیف برگشتی پیدا نشد.');
        }

        if (product['trackInventory'] as bool? ?? false) {
          await _insertMovement(
            txn,
            warehouseId: warehouseId,
            productId: productId,
            documentDate: _dateOnly(documentDate),
            movementType: 'SaleReturn',
            quantity: quantity,
            unitCost: unitCost,
            lotNumber: _text(original['lotNumber']),
            serialNumber: _text(original['serialNumber']),
            expiryDate: _text(original['expiryDate']),
            referenceType: 'SalesReturn',
            referenceId: returnId,
            description: 'برگشت از فروش ' + invoice['number'].toString(),
          );
        }

        grandTotal += netAmount + taxAmount;
        taxTotal += taxAmount;
        costTotal += costAmount;
        newReturnedByLine[lineId] =
            (newReturnedByLine[lineId] ?? 0) + quantity;

        postedLines.add({
          'id': _newId(),
          'salesInvoiceLineId': lineId,
          'productId': productId,
          'sku': product['sku'],
          'productName': product['name'],
          'unitName': product['unitName'] ?? 'عدد',
          'quantity': quantity,
          'netAmount': netAmount,
          'taxAmount': taxAmount,
          'unitCost': unitCost,
          'costAmount': costAmount,
        });
      }

      final settlementAmount = _money(grandTotal);
      final returnTaxAmount = _money(taxTotal);
      final netSalesAmount = settlementAmount - returnTaxAmount;
      final returnedCostAmount = _money(costTotal);
      final paymentType = invoice['paymentType']?.toString() ?? 'Cash';

      final journalLines = <_DemoJournalLine>[
        if (netSalesAmount > 0)
          _DemoJournalLine(
            accountId: _salesRevenueAccountId,
            description: 'برگشت درآمد فروش $number',
            debit: netSalesAmount,
          ),
        if (returnTaxAmount > 0)
          _DemoJournalLine(
            accountId: _salesTaxAccountId,
            description: 'برگشت مالیات فروش $number',
            debit: returnTaxAmount,
          ),
        _DemoJournalLine(
          accountId: paymentType == 'Cash'
              ? _cashAccountId
              : _receivablesAccountId,
          detailAccountId: paymentType == 'Credit'
              ? invoice['customerDetailAccountId']?.toString()
              : null,
          description: 'تسویه برگشت از فروش $number',
          credit: settlementAmount,
        ),
        if (returnedCostAmount > 0)
          _DemoJournalLine(
            accountId: _inventoryAccountId,
            description: 'برگشت موجودی فروش $number',
            debit: returnedCostAmount,
          ),
        if (returnedCostAmount > 0)
          _DemoJournalLine(
            accountId: _cogsAccountId,
            description: 'برگشت بهای تمام‌شده $number',
            credit: returnedCostAmount,
          ),
      ];

      final fiscalYearId = invoice['fiscalYearId']?.toString() ??
          '00000000-0000-4000-8000-000000000510';
      final journalNumber = await _insertPostedJournal(
        txn,
        fiscalYearId: fiscalYearId,
        documentDate: _dateOnly(documentDate),
        description: 'ثبت حسابداری برگشت از فروش $number',
        lines: journalLines,
      );

      var fullyReturned = true;
      for (final entry in originalLines.entries) {
        final originalQuantity = _num(entry.value['quantity']);
        final totalReturned =
            (returnedByLine[entry.key] ?? 0) +
            (newReturnedByLine[entry.key] ?? 0);
        if (totalReturned < originalQuantity) {
          fullyReturned = false;
          break;
        }
      }

      if (fullyReturned) {
        invoice['status'] = 'Reversed';
        invoice['reversalJournalNumber'] = journalNumber;
        await _upsertCachedEntity(
          txn,
          'SalesInvoice',
          invoiceId,
          invoice,
        );
      }

      final warehouse = warehouses[warehouseId]!;
      final posted = <String, dynamic>{
        'id': returnId,
        'salesInvoiceId': invoiceId,
        'salesInvoiceNumber': invoice['number'],
        'number': number,
        'documentDate': _dateOnly(documentDate),
        'warehouseId': warehouseId,
        'warehouseName': warehouse['name'],
        'reason': trimmedReason,
        'status': 'Posted',
        'currencyCode': 'BASE',
        'grandTotal': grandTotal,
        'taxTotal': taxTotal,
        'costTotal': costTotal,
        'accountingJournalNumber': journalNumber,
        'lines': postedLines,
      };

      await _upsertCachedEntity(
        txn,
        'SalesReturn',
        returnId,
        posted,
      );
      await _refreshStockCaches(txn);

      return posted;
    });
  }

  Future<void> _ensureCapacityForNewDocument(
    DatabaseExecutor txn,
  ) async {
    final accountingRows = await txn.rawQuery(
      'SELECT COUNT(*) AS count FROM local_accounting_documents '
      'WHERE company_id = ?',
      [DemoMode.companyId],
    );
    final draftRows = await txn.rawQuery(
      'SELECT COUNT(*) AS count FROM local_store_drafts '
      'WHERE company_id = ?',
      [DemoMode.companyId],
    );

    final accounting =
        (accountingRows.first['count'] as num?)?.toInt() ?? 0;
    final drafts = (draftRows.first['count'] as num?)?.toInt() ?? 0;

    if (accounting + drafts >= DemoMode.documentLimit) {
      throw StateError(
        'سقف ' +
            DemoMode.documentLimit.toString() +
            ' سند در حالت تست پر شده است.',
      );
    }
  }

  Future<Map<String, dynamic>> _readDraft(
    DatabaseExecutor txn,
    String draftId,
    String entityType,
  ) async {
    final rows = await txn.query(
      'local_store_drafts',
      columns: ['payload_json', 'sync_status'],
      where: 'id = ? AND company_id = ? AND entity_type = ?',
      whereArgs: [draftId, DemoMode.companyId, entityType],
      limit: 1,
    );

    if (rows.isEmpty) {
      throw StateError('پیش‌نویس محلی پیدا نشد یا قبلاً ثبت شده است.');
    }

    if (rows.first['sync_status'] != 'LocalOnly') {
      throw StateError('این پیش‌نویس متعلق به حالت تست محلی نیست.');
    }

    return Map<String, dynamic>.from(
      jsonDecode(rows.first['payload_json'] as String) as Map,
    );
  }

  Future<Map<String, Map<String, dynamic>>> _readCachedMap(
    DatabaseExecutor txn,
    String entityType,
  ) async {
    final list = await _readCachedList(txn, entityType);
    return {
      for (final item in list)
        if (item['id'] != null)
          item['id'].toString(): item,
    };
  }

  Future<List<Map<String, dynamic>>> _readCachedList(
    DatabaseExecutor txn,
    String entityType,
  ) async {
    final rows = await txn.query(
      'cached_store_entities',
      columns: ['payload_json'],
      where: 'company_id = ? AND entity_type = ?',
      whereArgs: [DemoMode.companyId, entityType],
    );

    return rows
        .map(
          (row) => Map<String, dynamic>.from(
            jsonDecode(row['payload_json'] as String) as Map,
          ),
        )
        .toList(growable: false);
  }

  Future<String?> _detailAccountName(
    DatabaseExecutor txn,
    String? id,
  ) async {
    if (id == null || id.isEmpty) return null;

    final rows = await txn.query(
      'cached_detail_accounts',
      columns: ['name'],
      where: 'id = ? AND company_id = ?',
      whereArgs: [id, DemoMode.companyId],
      limit: 1,
    );

    return rows.isEmpty ? null : rows.first['name']?.toString();
  }

  Future<_StockValue> _traceBalance(
    DatabaseExecutor txn, {
    required String warehouseId,
    required String productId,
    required String trackingMode,
    String? lotNumber,
    String? serialNumber,
    String? expiryDate,
  }) async {
    final rows = await txn.query(
      'demo_stock_movements',
      columns: [
        'quantity',
        'unit_cost',
        'lot_number',
        'serial_number',
        'expiry_date',
      ],
      where: 'company_id = ? AND warehouse_id = ? AND product_id = ?',
      whereArgs: [
        DemoMode.companyId,
        warehouseId,
        productId,
      ],
    );

    num quantity = 0;
    num value = 0;

    for (final row in rows) {
      if (trackingMode == 'Serial' &&
          row['serial_number']?.toString() != serialNumber) {
        continue;
      }
      if (trackingMode == 'Lot' &&
          (row['lot_number']?.toString() != lotNumber ||
              row['expiry_date']?.toString() != expiryDate)) {
        continue;
      }

      final q = _num(row['quantity']);
      final cost = _num(row['unit_cost']);
      quantity += q;
      value += q * cost;
    }

    return _StockValue(quantity, value);
  }

  Future<num> _serialQuantity(
    DatabaseExecutor txn,
    String productId,
    String serialNumber,
  ) async {
    final rows = await txn.query(
      'demo_stock_movements',
      columns: ['quantity'],
      where: 'company_id = ? AND product_id = ? AND serial_number = ?',
      whereArgs: [
        DemoMode.companyId,
        productId,
        serialNumber,
      ],
    );

    return rows.fold<num>(
      0,
      (sum, row) => sum + _num(row['quantity']),
    );
  }

  void _validateTrace(
    Map<String, dynamic> product,
    Map<String, dynamic> line,
    num quantity,
  ) {
    if (!(product['trackInventory'] as bool? ?? false)) return;

    final mode = product['trackingMode']?.toString() ?? 'None';
    if (mode == 'Lot' && _text(line['lotNumber']) == null) {
      throw StateError(
        'شماره Lot برای «' + product['name'].toString() + '» الزامی است.',
      );
    }

    if (mode == 'Serial') {
      if (_text(line['serialNumber']) == null) {
        throw StateError(
          'شماره سریال برای «' +
              product['name'].toString() +
              '» الزامی است.',
        );
      }
      if (quantity != 1) {
        throw StateError(
          'کالای سریالی باید در هر ردیف با تعداد ۱ ثبت شود.',
        );
      }
    }
  }

  Future<void> _insertMovement(
    DatabaseExecutor txn, {
    required String warehouseId,
    required String productId,
    required String documentDate,
    required String movementType,
    required num quantity,
    required num unitCost,
    String? lotNumber,
    String? serialNumber,
    String? expiryDate,
    String? referenceType,
    String? referenceId,
    String? description,
  }) async {
    await txn.insert(
      'demo_stock_movements',
      {
        'id': _newId(),
        'company_id': DemoMode.companyId,
        'warehouse_id': warehouseId,
        'product_id': productId,
        'document_date': documentDate,
        'movement_type': movementType,
        'quantity': quantity,
        'unit_cost': unitCost,
        'lot_number': lotNumber,
        'serial_number': serialNumber,
        'expiry_date': expiryDate,
        'reference_type': referenceType,
        'reference_id': referenceId,
        'description': description,
        'created_at': DateTime.now().toUtc().toIso8601String(),
      },
    );
  }

  Future<String> _insertPostedJournal(
    DatabaseExecutor txn, {
    required String fiscalYearId,
    required String documentDate,
    required String description,
    required List<_DemoJournalLine> lines,
  }) async {
    final debit = lines.fold<int>(
      0,
      (sum, line) => sum + line.debit,
    );
    final credit = lines.fold<int>(
      0,
      (sum, line) => sum + line.credit,
    );

    if (debit <= 0 || debit != credit) {
      throw StateError('سند حسابداری تولیدشده توسط موتور دمو تراز نیست.');
    }

    final countRows = await txn.rawQuery(
      'SELECT COUNT(*) AS count FROM local_accounting_documents '
      'WHERE company_id = ?',
      [DemoMode.companyId],
    );
    final sequence =
        ((countRows.first['count'] as num?)?.toInt() ?? 0) + 1;
    final number =
        'DEMO-J-' + sequence.toString().padLeft(6, '0');
    final documentId = _newId();
    final now = DateTime.now().toUtc().toIso8601String();

    await txn.insert(
      'local_accounting_documents',
      {
        'id': documentId,
        'company_id': DemoMode.companyId,
        'fiscal_year_id': fiscalYearId,
        'server_id': null,
        'server_number': number,
        'document_date': documentDate,
        'description': description,
        'status': 'PostedLocal',
        'sync_status': 'LocalOnly',
        'currency': 'IRR',
        'created_at': now,
        'updated_at': now,
      },
    );

    for (var index = 0; index < lines.length; index++) {
      final line = lines[index];
      await txn.insert(
        'local_document_lines',
        {
          'id': _newId(),
          'document_id': documentId,
          'account_id': line.accountId,
          'detail_account_id': line.detailAccountId,
          'cost_center_id': null,
          'project_id': null,
          'currency_id': null,
          'foreign_debit': null,
          'foreign_credit': null,
          'exchange_rate': null,
          'description': line.description,
          'debit': line.debit,
          'credit': line.credit,
          'sort_order': index,
        },
      );
    }

    return number;
  }

  Future<String> _nextCachedNumber(
    DatabaseExecutor txn, {
    required String entityType,
    required String prefix,
  }) async {
    final rows = await txn.rawQuery(
      'SELECT COUNT(*) AS count FROM cached_store_entities '
      'WHERE company_id = ? AND entity_type = ?',
      [DemoMode.companyId, entityType],
    );
    final next = ((rows.first['count'] as num?)?.toInt() ?? 0) + 1;
    return prefix + next.toString().padLeft(6, '0');
  }

  Future<void> _upsertCachedEntity(
    DatabaseExecutor txn,
    String entityType,
    String entityId,
    Map<String, dynamic> payload,
  ) async {
    await txn.insert(
      'cached_store_entities',
      {
        'company_id': DemoMode.companyId,
        'entity_type': entityType,
        'entity_id': entityId,
        'payload_json': jsonEncode(payload),
        'updated_at': DateTime.now().toUtc().toIso8601String(),
      },
      conflictAlgorithm: ConflictAlgorithm.replace,
    );
  }

  Future<void> _finishDraft(
    DatabaseExecutor txn,
    String draftId,
  ) async {
    await txn.delete(
      'sync_outbox',
      where: 'company_id = ? AND entity_id = ?',
      whereArgs: [DemoMode.companyId, draftId],
    );
    await txn.delete(
      'local_store_drafts',
      where: 'company_id = ? AND id = ?',
      whereArgs: [DemoMode.companyId, draftId],
    );
  }

  Future<void> _refreshStockCaches(
    DatabaseExecutor txn,
  ) async {
    final products = await _readCachedMap(txn, 'StoreProduct');
    final warehouses = await _readCachedMap(txn, 'Warehouse');

    final movements = await txn.query(
      'demo_stock_movements',
      where: 'company_id = ?',
      whereArgs: [DemoMode.companyId],
    );

    final balances = <Map<String, dynamic>>[];
    final lowStock = <Map<String, dynamic>>[];

    for (final warehouse in warehouses.values) {
      if (!(warehouse['isActive'] as bool? ?? true)) continue;
      final warehouseId = warehouse['id'].toString();

      for (final product in products.values) {
        if (!(product['isActive'] as bool? ?? true) ||
            !(product['trackInventory'] as bool? ?? false)) {
          continue;
        }

        final productId = product['id'].toString();
        num quantity = 0;
        num value = 0;

        for (final movement in movements) {
          if (movement['warehouse_id']?.toString() != warehouseId ||
              movement['product_id']?.toString() != productId) {
            continue;
          }

          final q = _num(movement['quantity']);
          quantity += q;
          value += q * _num(movement['unit_cost']);
        }

        final averageCost =
            quantity == 0 ? 0 : value / quantity;
        final minimumStock = _num(product['minimumStock']);
        final isLow =
            minimumStock > 0 && quantity <= minimumStock;

        final row = <String, dynamic>{
          'productId': productId,
          'sku': product['sku'],
          'productName': product['name'],
          'warehouseId': warehouseId,
          'warehouseName': warehouse['name'],
          'quantity': quantity,
          'averageCost': averageCost,
          'inventoryValue': value,
          'minimumStock': minimumStock,
          'isLowStock': isLow,
        };
        balances.add(row);

        if (isLow) {
          lowStock.add({
            ...row,
            'shortage': max<num>(0, minimumStock - quantity),
          });
        }
      }
    }

    final traceGroups = <String, Map<String, dynamic>>{};

    for (final movement in movements) {
      final productId = movement['product_id']?.toString();
      final warehouseId = movement['warehouse_id']?.toString();
      final product = productId == null ? null : products[productId];
      final warehouse =
          warehouseId == null ? null : warehouses[warehouseId];

      if (product == null || warehouse == null) continue;

      final mode = product['trackingMode']?.toString() ?? 'None';
      if (mode == 'None') continue;

      final lot = movement['lot_number']?.toString();
      final serial = movement['serial_number']?.toString();
      final expiry = movement['expiry_date']?.toString();
      final key = [
        productId,
        warehouseId,
        lot ?? '',
        serial ?? '',
        expiry ?? '',
      ].join('|');

      final group = traceGroups.putIfAbsent(
        key,
        () => {
          'productId': productId,
          'sku': product['sku'],
          'productName': product['name'],
          'warehouseId': warehouseId,
          'warehouseName': warehouse['name'],
          'lotNumber': lot,
          'serialNumber': serial,
          'expiryDate': expiry,
          'quantity': 0.0,
          'value': 0.0,
        },
      );

      final q = _num(movement['quantity']);
      group['quantity'] = _num(group['quantity']) + q;
      group['value'] =
          _num(group['value']) + q * _num(movement['unit_cost']);
    }

    final traces = traceGroups.values
        .where((group) => _num(group['quantity']) != 0)
        .map((group) {
          final quantity = _num(group['quantity']);
          final value = _num(group['value']);
          return <String, dynamic>{
            'productId': group['productId'],
            'sku': group['sku'],
            'productName': group['productName'],
            'warehouseId': group['warehouseId'],
            'warehouseName': group['warehouseName'],
            'lotNumber': group['lotNumber'],
            'serialNumber': group['serialNumber'],
            'expiryDate': group['expiryDate'],
            'quantity': quantity,
            'averageCost': quantity == 0 ? 0 : value / quantity,
          };
        })
        .toList(growable: false);

    await _replaceCachedList(
      txn,
      'StockBalance',
      balances,
      (item) =>
          item['productId'].toString() +
          '|' +
          item['warehouseId'].toString(),
    );
    await _replaceCachedList(
      txn,
      'LowStockAlert',
      lowStock,
      (item) =>
          item['productId'].toString() +
          '|' +
          item['warehouseId'].toString(),
    );
    await _replaceCachedList(
      txn,
      'StockTrace',
      traces,
      (item) => [
        item['productId'],
        item['warehouseId'],
        item['lotNumber'] ?? '',
        item['serialNumber'] ?? '',
        item['expiryDate'] ?? '',
      ].join('|'),
    );
  }

  Future<void> _replaceCachedList(
    DatabaseExecutor txn,
    String entityType,
    List<Map<String, dynamic>> items,
    String Function(Map<String, dynamic>) keyOf,
  ) async {
    await txn.delete(
      'cached_store_entities',
      where: 'company_id = ? AND entity_type = ?',
      whereArgs: [DemoMode.companyId, entityType],
    );

    final now = DateTime.now().toUtc().toIso8601String();
    for (final item in items) {
      await txn.insert(
        'cached_store_entities',
        {
          'company_id': DemoMode.companyId,
          'entity_type': entityType,
          'entity_id': keyOf(item),
          'payload_json': jsonEncode(item),
          'updated_at': now,
        },
        conflictAlgorithm: ConflictAlgorithm.replace,
      );
    }
  }

  int _money(num value) => value.round();

  static num _num(Object? value) {
    if (value is num) return value;
    return num.tryParse(value?.toString() ?? '') ?? 0;
  }

  static String? _text(Object? value) {
    final text = value?.toString().trim();
    return text == null || text.isEmpty ? null : text;
  }

  static String _plain(num value) {
    if (value == value.roundToDouble()) {
      return value.toInt().toString();
    }
    return value.toStringAsFixed(3);
  }

  String _newId() {
    final time = DateTime.now().microsecondsSinceEpoch.toRadixString(16);
    final random = List.generate(
      4,
      (_) => _random.nextInt(0x100000000)
          .toRadixString(16)
          .padLeft(8, '0'),
    ).join();
    return (time + random).substring(0, 32);
  }

  static String _dateOnly(DateTime value) {
    return value.year.toString().padLeft(4, '0') +
        '-' +
        value.month.toString().padLeft(2, '0') +
        '-' +
        value.day.toString().padLeft(2, '0');
  }
}

class _StockValue {
  const _StockValue(this.quantity, this.value);

  final num quantity;
  final num value;
}

class _DemoJournalLine {
  const _DemoJournalLine({
    required this.accountId,
    required this.description,
    this.detailAccountId,
    this.debit = 0,
    this.credit = 0,
  });

  final String accountId;
  final String description;
  final String? detailAccountId;
  final int debit;
  final int credit;
}
