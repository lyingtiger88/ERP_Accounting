import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../accounting/report_support.dart';

class ProductsPage extends StatefulWidget {
  const ProductsPage({
    super.key,
    required this.accessToken,
  });

  final String accessToken;

  @override
  State<ProductsPage> createState() => _ProductsPageState();
}

class _ProductsPageState extends State<ProductsPage> {
  final _apiClient = ApiClient();
  late Future<List<Map<String, dynamic>>> _future;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _reload();
  }

  void _reload() {
    _future = _apiClient.getStoreProducts(
      bearerToken: widget.accessToken,
    );
  }

  Future<void> _openEditor([Map<String, dynamic>? existing]) async {
    final sku = TextEditingController(
      text: existing?['sku']?.toString() ?? '',
    );
    final name = TextEditingController(
      text: existing?['name']?.toString() ?? '',
    );
    final barcode = TextEditingController(
      text: existing?['barcode']?.toString() ?? '',
    );
    final unit = TextEditingController(
      text: existing?['unitName']?.toString() ?? 'عدد',
    );
    final salesPrice = TextEditingController(
      text: existing == null
          ? ''
          : reportNumber(existing['salesPrice']).toString(),
    );
    final purchasePrice = TextEditingController(
      text: existing == null
          ? ''
          : reportNumber(
              existing['defaultPurchasePrice'],
            ).toString(),
    );
    final minimumStock = TextEditingController(
      text: existing == null
          ? '0'
          : reportNumber(
              existing['minimumStock'],
            ).toString(),
    );

    var kind = existing?['kind']?.toString() ?? 'Inventory';
    var trackInventory =
        existing?['trackInventory'] as bool? ?? true;
    var trackingMode =
        existing?['trackingMode']?.toString() ?? 'None';
    var isActive = existing?['isActive'] as bool? ?? true;

    final saved = await showDialog<bool>(
      context: context,
      builder: (context) {
        return StatefulBuilder(
          builder: (context, setDialogState) {
            final inventoryKind = kind == 'Inventory';

            return AlertDialog(
              title: Text(
                existing == null
                    ? 'کالا / خدمت جدید'
                    : 'ویرایش کالا / خدمت',
              ),
              content: SizedBox(
                width: 520,
                child: SingleChildScrollView(
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      TextField(
                        controller: sku,
                        textDirection: TextDirection.ltr,
                        decoration: const InputDecoration(
                          labelText: 'کد کالا / SKU',
                        ),
                      ),
                      const SizedBox(height: 12),
                      TextField(
                        controller: name,
                        decoration: const InputDecoration(
                          labelText: 'نام کالا یا خدمت',
                        ),
                      ),
                      const SizedBox(height: 12),
                      TextField(
                        controller: barcode,
                        textDirection: TextDirection.ltr,
                        decoration: const InputDecoration(
                          labelText: 'بارکد (اختیاری)',
                        ),
                      ),
                      const SizedBox(height: 12),
                      TextField(
                        controller: unit,
                        decoration: const InputDecoration(
                          labelText: 'واحد',
                        ),
                      ),
                      const SizedBox(height: 12),
                      DropdownButtonFormField<String>(
                        initialValue: kind,
                        decoration: const InputDecoration(
                          labelText: 'نوع',
                        ),
                        items: const [
                          DropdownMenuItem(
                            value: 'Inventory',
                            child: Text('کالا / موجودی‌پذیر'),
                          ),
                          DropdownMenuItem(
                            value: 'Service',
                            child: Text('خدمت'),
                          ),
                        ],
                        onChanged: (value) {
                          if (value == null) return;
                          setDialogState(() {
                            kind = value;
                            if (kind == 'Service') {
                              trackInventory = false;
                            }
                          });
                        },
                      ),
                      const SizedBox(height: 12),
                      TextField(
                        controller: salesPrice,
                        keyboardType:
                            const TextInputType.numberWithOptions(
                          decimal: true,
                        ),
                        textDirection: TextDirection.ltr,
                        decoration: const InputDecoration(
                          labelText: 'قیمت فروش پیش‌فرض (ریال)',
                        ),
                      ),
                      const SizedBox(height: 12),
                      TextField(
                        controller: purchasePrice,
                        keyboardType:
                            const TextInputType.numberWithOptions(
                          decimal: true,
                        ),
                        textDirection: TextDirection.ltr,
                        decoration: const InputDecoration(
                          labelText: 'بهای خرید / بهای اولیه (ریال)',
                        ),
                      ),
                      const SizedBox(height: 8),
                      SwitchListTile(
                        contentPadding: EdgeInsets.zero,
                        title: const Text('کنترل موجودی'),
                        subtitle: const Text(
                          'برای خدمات غیرفعال است.',
                        ),
                        value: inventoryKind && trackInventory,
                        onChanged: inventoryKind
                            ? (value) {
                                setDialogState(
                                  () => trackInventory = value,
                                );
                              }
                            : null,
                      ),
                      if (inventoryKind &&
                          trackInventory) ...[
                        const SizedBox(height: 8),
                        DropdownButtonFormField<String>(
                          initialValue: trackingMode,
                          decoration: const InputDecoration(
                            labelText: 'رهگیری موجودی',
                          ),
                          items: const [
                            DropdownMenuItem(
                              value: 'None',
                              child: Text('بدون لات / سریال'),
                            ),
                            DropdownMenuItem(
                              value: 'Lot',
                              child: Text('لات / بچ'),
                            ),
                            DropdownMenuItem(
                              value: 'Serial',
                              child: Text('سریال یکتا'),
                            ),
                          ],
                          onChanged: (value) {
                            if (value != null) {
                              setDialogState(
                                () => trackingMode = value,
                              );
                            }
                          },
                        ),
                        const SizedBox(height: 12),
                        TextField(
                          controller: minimumStock,
                          keyboardType:
                              const TextInputType.numberWithOptions(
                            decimal: true,
                          ),
                          textDirection: TextDirection.ltr,
                          decoration: const InputDecoration(
                            labelText:
                                'حداقل موجودی / نقطه هشدار',
                          ),
                        ),
                      ],
                      if (existing != null)
                        SwitchListTile(
                          contentPadding: EdgeInsets.zero,
                          title: const Text('فعال'),
                          value: isActive,
                          onChanged: (value) {
                            setDialogState(
                              () => isActive = value,
                            );
                          },
                        ),
                    ],
                  ),
                ),
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.pop(context, false),
                  child: const Text('انصراف'),
                ),
                FilledButton(
                  onPressed: () {
                    if (sku.text.trim().isEmpty ||
                        name.text.trim().isEmpty ||
                        unit.text.trim().isEmpty) {
                      return;
                    }

                    Navigator.pop(context, true);
                  },
                  child: const Text('ذخیره'),
                ),
              ],
            );
          },
        );
      },
    );

    if (saved != true) {
      sku.dispose();
      name.dispose();
      barcode.dispose();
      unit.dispose();
      salesPrice.dispose();
      purchasePrice.dispose();
      minimumStock.dispose();
      return;
    }

    final sales =
        _parseNumber(salesPrice.text) ?? 0;
    final purchase =
        _parseNumber(purchasePrice.text) ?? 0;
    final minStock =
        _parseNumber(minimumStock.text) ?? 0;

    setState(() => _busy = true);

    try {
      if (existing == null) {
        await _apiClient.createStoreProduct(
          bearerToken: widget.accessToken,
          sku: sku.text.trim(),
          name: name.text.trim(),
          barcode: _nullIfBlank(barcode.text),
          unitName: unit.text.trim(),
          kind: kind,
          trackInventory: trackInventory,
          salesPrice: sales,
          defaultPurchasePrice: purchase,
          trackingMode: trackingMode,
          minimumStock: minStock,
        );
      } else {
        await _apiClient.updateStoreProduct(
          bearerToken: widget.accessToken,
          productId: existing['id'].toString(),
          sku: sku.text.trim(),
          name: name.text.trim(),
          barcode: _nullIfBlank(barcode.text),
          unitName: unit.text.trim(),
          kind: kind,
          trackInventory: trackInventory,
          salesPrice: sales,
          defaultPurchasePrice: purchase,
          isActive: isActive,
          trackingMode: trackingMode,
          minimumStock: minStock,
        );
      }

      if (!mounted) return;
      setState(_reload);
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      sku.dispose();
      name.dispose();
      barcode.dispose();
      unit.dispose();
      salesPrice.dispose();
      purchasePrice.dispose();
      minimumStock.dispose();

      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _lookupBarcode() async {
    final controller = TextEditingController();

    final code = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('جست‌وجوی بارکد / SKU'),
        content: TextField(
          controller: controller,
          autofocus: true,
          textDirection: TextDirection.ltr,
          decoration: const InputDecoration(
            labelText: 'بارکد را اسکن کنید',
            helperText:
                'بارکدخوان USB معمولاً مانند کیبورد مقدار را وارد و Enter ارسال می‌کند.',
          ),
          onSubmitted: (value) =>
              Navigator.pop(context, value.trim()),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('انصراف'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(
              context,
              controller.text.trim(),
            ),
            child: const Text('جست‌وجو'),
          ),
        ],
      ),
    );

    controller.dispose();

    if (code == null || code.isEmpty) return;

    try {
      final product = await _apiClient.findStoreProduct(
        bearerToken: widget.accessToken,
        code: code,
      );

      if (product == null) {
        _message('کالایی با این بارکد یا SKU پیدا نشد.');
        return;
      }

      if (!mounted) return;
      await _openEditor(product);
    } on ApiException catch (error) {
      _message(error.message);
    }
  }

  num? _parseNumber(String value) {
    return num.tryParse(
      value.replaceAll(',', '').trim(),
    );
  }

  String? _nullIfBlank(String value) {
    final trimmed = value.trim();
    return trimmed.isEmpty ? null : trimmed;
  }

  void _message(String value) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(value)),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('کالاها و خدمات'),
          actions: [
            IconButton(
              tooltip: 'بارکد / SKU',
              onPressed: _busy ? null : _lookupBarcode,
              icon: const Icon(Icons.qr_code_scanner_outlined),
            ),
            IconButton(
              tooltip: 'بازخوانی',
              onPressed: _busy
                  ? null
                  : () => setState(_reload),
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        floatingActionButton: FloatingActionButton.extended(
          onPressed: _busy ? null : _openEditor,
          icon: const Icon(Icons.add_box_outlined),
          label: const Text('کالا / خدمت جدید'),
        ),
        body: FutureBuilder<List<Map<String, dynamic>>>(
          future: _future,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Center(
                child: CircularProgressIndicator(),
              );
            }

            if (snapshot.hasError) {
              return Center(
                child: Text(snapshot.error.toString()),
              );
            }

            final items =
                snapshot.data ?? const <Map<String, dynamic>>[];

            if (items.isEmpty) {
              return const Center(
                child: Text(
                  'هنوز کالا یا خدمتی تعریف نشده است.',
                ),
              );
            }

            return ListView.separated(
              padding: const EdgeInsets.all(20),
              itemCount: items.length,
              separatorBuilder: (_, __) =>
                  const SizedBox(height: 10),
              itemBuilder: (context, index) {
                final item = items[index];
                final inventory =
                    item['kind'].toString() == 'Inventory';

                return Card(
                  child: ListTile(
                    onTap: _busy
                        ? null
                        : () => _openEditor(item),
                    leading: CircleAvatar(
                      child: Icon(
                        inventory
                            ? Icons.inventory_2_outlined
                            : Icons.miscellaneous_services_outlined,
                      ),
                    ),
                    title: Text(
                      item['sku'].toString() +
                          ' — ' +
                          item['name'].toString(),
                    ),
                    subtitle: Text(
                      'واحد: ' +
                          item['unitName'].toString() +
                          ' • فروش: ' +
                          formatReportMoney(
                            reportNumber(item['salesPrice']),
                          ) +
                          ' ریال' +
                          (inventory &&
                                  reportNumber(
                                        item['minimumStock'],
                                      ) >
                                      0
                              ? ' • حداقل: ' +
                                  formatReportMoney(
                                    reportNumber(
                                      item['minimumStock'],
                                    ),
                                  )
                              : ''),
                    ),
                    trailing: Wrap(
                      spacing: 8,
                      crossAxisAlignment:
                          WrapCrossAlignment.center,
                      children: [
                        Chip(
                          label: Text(
                            inventory ? 'کالا' : 'خدمت',
                          ),
                        ),
                        if (inventory &&
                            item['trackingMode']?.toString() !=
                                'None')
                          Chip(
                            label: Text(
                              item['trackingMode'].toString() ==
                                      'Serial'
                                  ? 'سریال'
                                  : 'لات',
                            ),
                          ),
                        if (!(item['isActive'] as bool? ?? true))
                          const Chip(
                            label: Text('غیرفعال'),
                          ),
                        const Icon(Icons.edit_outlined),
                      ],
                    ),
                  ),
                );
              },
            );
          },
        ),
      ),
    );
  }
}
