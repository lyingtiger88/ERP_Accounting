import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/date/jalali_date.dart';

class AuditTrailPage extends StatefulWidget {
  const AuditTrailPage({
    super.key,
    required this.accessToken,
    this.entityId,
    this.title,
  });

  final String accessToken;
  final String? entityId;
  final String? title;

  @override
  State<AuditTrailPage> createState() => _AuditTrailPageState();
}

class _AuditTrailPageState extends State<AuditTrailPage> {
  final _apiClient = ApiClient();
  late Future<List<Map<String, dynamic>>> _future;

  @override
  void initState() {
    super.initState();
    _reload();
  }

  void _reload() {
    _future = _apiClient.getAccountingAudit(
      bearerToken: widget.accessToken,
      entityId: widget.entityId,
      limit: 200,
    );
  }

  String _actionTitle(String action) {
    switch (action) {
      case 'POST':
        return 'ثبت قطعی سند';
      case 'POST_SYNC':
        return 'ثبت قطعی از همگام‌سازی';
      case 'REVERSE':
        return 'برگشت سند';
      case 'REVERSAL_POST':
        return 'ثبت سند معکوس';
      default:
        return action;
    }
  }

  IconData _actionIcon(String action) {
    switch (action) {
      case 'POST':
      case 'POST_SYNC':
        return Icons.check_circle_outline;
      case 'REVERSE':
        return Icons.undo_outlined;
      case 'REVERSAL_POST':
        return Icons.history_outlined;
      default:
        return Icons.history_toggle_off_outlined;
    }
  }

  String _timeText(String value) {
    final date = DateTime.parse(value).toLocal();
    final jalali = JalaliDate.fromGregorian(date).formatted;
    final hour = date.hour.toString().padLeft(2, '0');
    final minute = date.minute.toString().padLeft(2, '0');
    return jalali + '  ' + hour + ':' + minute;
  }

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: Text(widget.title ?? 'تاریخچه حسابرسی'),
          actions: [
            IconButton(
              tooltip: 'بازخوانی',
              onPressed: () => setState(_reload),
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        body: FutureBuilder<List<Map<String, dynamic>>>(
          future: _future,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Center(child: CircularProgressIndicator());
            }

            if (snapshot.hasError) {
              return Center(
                child: Padding(
                  padding: const EdgeInsets.all(24),
                  child: Text(
                    'دریافت تاریخچه حسابرسی ناموفق بود: ' +
                        snapshot.error.toString(),
                  ),
                ),
              );
            }

            final items =
                snapshot.data ?? const <Map<String, dynamic>>[];

            if (items.isEmpty) {
              return const Center(
                child: Text('رویداد حسابرسی ثبت نشده است.'),
              );
            }

            return ListView.separated(
              padding: const EdgeInsets.all(20),
              itemCount: items.length,
              separatorBuilder: (_, __) => const SizedBox(height: 10),
              itemBuilder: (context, index) {
                final item = items[index];
                final action = item['action'].toString();
                final reason = item['reason'] as String?;
                final actor =
                    item['userDisplayName']?.toString() ?? 'کاربر نامشخص';

                return Card(
                  child: ListTile(
                    leading: CircleAvatar(
                      child: Icon(_actionIcon(action)),
                    ),
                    title: Text(_actionTitle(action)),
                    subtitle: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        const SizedBox(height: 4),
                        Text(
                          actor +
                              ' • ' +
                              _timeText(item['createdAt'].toString()),
                        ),
                        if (reason != null && reason.isNotEmpty) ...[
                          const SizedBox(height: 4),
                          Text('علت: ' + reason),
                        ],
                      ],
                    ),
                    trailing: widget.entityId == null
                        ? Tooltip(
                            message: item['entityId'].toString(),
                            child: const Icon(
                              Icons.receipt_long_outlined,
                            ),
                          )
                        : null,
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
