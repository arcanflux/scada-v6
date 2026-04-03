// Sawtooth Detector — скрипт для Rapid SCADA v6
// Детектор пилообразного сигнала.
//
// Назначение:
//   Формула ставится на ОТСЛЕЖИВАЕМЫЙ канал (температура).
//   Скрипт анализирует входящие значения и записывает результат (1 или 0)
//   в отдельный канал-индикатор через SetData.
//   Значение самого канала температуры проходит без изменений.
//
// Использование:
//   1. Добавьте этот скрипт в таблицу «Скрипты» проекта Rapid SCADA.
//   2. Создайте канал-индикатор (тип «Вычисляемый») для статуса пилы.
//   3. В формулу входного канала (InFormula) канала ТЕМПЕРАТУРЫ впишите:
//        SawtoothCheck(200)
//      где 200 — номер канала-индикатора, куда запишется результат (1/0).
//
//   Параметры:
//     - Порог скачка: 10 градусов (threshold)
//     - Таймаут сброса: 60 минут (timeoutMinutes)
//     - Задержка старта: 5 минут (startDelayMinutes)
//
//   Примеры:
//     SawtoothCheck(200)              — всё по умолчанию
//     SawtoothCheck(200, 10, 60, 15)  — задержка старта 15 минут
//     SawtoothCheck(200, 15, 30, 10)  — порог 15°, таймаут 30 мин, задержка 10 мин
//
//   Канал температуры продолжает показывать температуру как обычно.
//   Канал-индикатор (200) будет показывать:
//     1 — пилообразный сигнал обнаружен
//     0 — пилообразного сигнала нет

// Словарь: номер канала температуры → предыдущее значение.
protected Dictionary<int, double> SawPrevValues = new Dictionary<int, double>();

// Словарь: номер канала температуры → время последнего скачка (UTC).
protected Dictionary<int, DateTime> SawLastJumpTimes = new Dictionary<int, DateTime>();

// Словарь: номер канала температуры → текущий статус пилы.
protected Dictionary<int, bool> SawActiveFlags = new Dictionary<int, bool>();

// Словарь: номер канала температуры → время первого вызова (UTC).
protected Dictionary<int, DateTime> SawStartTimes = new Dictionary<int, DateTime>();

/// <summary>
/// Ставится на канал температуры (InFormula).
/// Анализирует значения на пилообразность и записывает результат 1/0 в outputCnlNum.
/// Возвращает исходное значение канала без изменений.
/// </summary>
/// <param name="outputCnlNum">Номер канала-индикатора для записи результата.</param>
/// <param name="threshold">Порог скачка в градусах (по умолчанию 10).</param>
/// <param name="timeoutMinutes">Минут без скачков для сброса (по умолчанию 60).</param>
/// <param name="startDelayMinutes">Задержка старта в минутах для избежания ложных срабатываний (по умолчанию 5).</param>
public CnlData SawtoothCheck(int outputCnlNum, double threshold = 10.0, double timeoutMinutes = 60.0, double startDelayMinutes = 5.0)
{
    // Работаем только с текущими данными.
    if (!IsCurrent)
        return CnlData;

    // Если данные канала не определены — пропускаем.
    if (CnlStat <= 0)
        return CnlData;

    double currentValue = CnlVal;
    DateTime now = Timestamp;
    int srcCnl = CnlNum;

    // Запоминаем время первого вызова для этого канала.
    if (!SawStartTimes.ContainsKey(srcCnl))
        SawStartTimes[srcCnl] = now;

    // Период прогрева: собираем данные, но не активируем пилу.
    bool isWarming = (now - SawStartTimes[srcCnl]).TotalMinutes < startDelayMinutes;

    // Запоминаем текущее значение (всегда, даже во время прогрева).
    SawPrevValues.TryGetValue(srcCnl, out double prevValue);
    bool hasPrev = SawPrevValues.ContainsKey(srcCnl);
    SawPrevValues[srcCnl] = currentValue;

    // Во время прогрева — всегда выдаём 0, только копим данные.
    if (isWarming)
    {
        SawActiveFlags[srcCnl] = false;
        SetData(outputCnlNum, 0.0, CnlStatusID.Defined);
        return CnlData;
    }

    bool isActive = SawActiveFlags.ContainsKey(srcCnl) && SawActiveFlags[srcCnl];

    // Сравниваем с предыдущим значением.
    if (hasPrev)
    {
        double delta = Math.Abs(currentValue - prevValue);

        if (delta >= threshold)
        {
            // Скачок обнаружен — пила активна.
            isActive = true;
            SawLastJumpTimes[srcCnl] = now;
        }
    }

    // Проверяем таймаут сброса.
    if (isActive && SawLastJumpTimes.TryGetValue(srcCnl, out DateTime lastJumpTime))
    {
        if ((now - lastJumpTime).TotalMinutes >= timeoutMinutes)
            isActive = false;
    }

    SawActiveFlags[srcCnl] = isActive;

    // Записываем результат (1/0) в канал-индикатор.
    SetData(outputCnlNum, isActive ? 1.0 : 0.0, CnlStatusID.Defined);

    // Возвращаем исходные данные канала температуры без изменений.
    return CnlData;
}
