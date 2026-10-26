# OrderEntry.4gl 程式分析報告

> 預期輸出範例。依單支原始碼人工整理，供討論 Markdown 應有的內容與深度。這不是現版分析器自動產生的結果，也不表示已執行或編譯此 4GL 程式。

## 1. 分析範圍與判讀方式

| 項目 | 內容 |
| --- | --- |
| 來源 | [samples/4gl/OrderEntry.4gl](../../samples/4gl/OrderEntry.4gl) |
| 分析日期 | 2026-10-26 |
| 原始檔 SHA-256 | `2697D1E6306C1A3727E454A5EF789ADDF949F7A8216941628D669463840829F5` |
| 原始碼行數 | 78 行，包含註解與空白行 |
| 語言判讀 | Informix 4GL 風格，實際編譯器、方言與版本未提供 |
| 資料庫宣告 | `DATABASE legacy_orders`，L2 |
| 程式單元 | `MAIN` 及 2 個 FUNCTION，沒有 REPORT 區塊 |
| SQL 範圍 | 7 處靜態 SQL，涉及 4 個資料表名稱與 15 個不同的「資料表．欄位」組合 |
| 外部資料 | 未使用資料字典、資料表 DDL、其他程式或實際資料庫內容 |

行號均對應上述來源版本。7 處 SQL 是原始碼中的敘述位置數量，不代表每次執行都會執行 7 次 SQL。`DATABASE` 與交易控制另列，不計入此數量。

本報告使用以下標示：

- **程式可確認**：原始碼直接出現的宣告、條件、計算、呼叫或資料讀寫。
- **依上下文推測**：依名稱、訊息與使用方式提出的解釋，尚未取得正式業務定義。
- **待確認**：本檔缺少足夠證據，或結果取決於方言、資料庫結構及執行環境。

本報告描述程式用途、流程、規則、SQL 與欄位，不包含轉譯、跨檔整合或多程式統計歸類。

## 2. 程式用途

**依原始碼可判讀，這是一支互動式訂單輸入範例。** 程式取得客戶編號、商品編號與數量，呼叫本檔函式進行檢核及讀取單價，再以「單價乘數量」計算金額。預期寫入一筆訂單主檔與一筆訂單明細，同時更新客戶的最近訂購日期。原碼安排了提交與回滾分支，但資料實際寫入範圍、訂單編號的取得，以及錯誤時是否能走到回滾，都有需確認的條件。

用途依據：L1 的範例註解、L13 的 `New Order Entry`、L14 的輸入、L16–L26 的檢核與計價，以及 L28–L40 的資料寫入與交易敘述。

### 2.1 輸入、處理與輸出

| 類別 | 內容 | 來源 |
| --- | --- | --- |
| 使用者輸入 | `customer_id`、`product_id`、`order_qty` | L14 |
| 檢核資料 | `customer.status`、`product.status` | L57、L65 |
| 計價資料 | `product.unit_price` | L76 |
| 計算 | `order_total = unit_price * order_qty` | L26 |
| 預期新增 | `orders` 主檔與 `order_detail` 明細 | L29、L31 |
| 預期更新 | `customer.last_order_date` | L32，篩選條件有同名歧義 |
| 成功路徑訊息 | `Order created:` 加上 `order_id` | L40 |
| 拒絕／失敗訊息 | 檢核回傳訊息，或 `Order creation failed` | L20、L36 |

本檔每次主流程只取得一組商品與數量，沒有輸入多筆商品明細的迴圈。未見檔案輸出或報表輸出。

### 2.2 本檔未呈現的業務處理

未見庫存查詢或扣減、信用額度檢核、折扣、稅額、付款或配送處理。這只表示本檔沒有這些實作，不代表業務必須包含它們，也不能推定其他程式是否有處理。

## 3. 程式結構與呼叫關係

```text
MAIN，L4–L42
  ├─ 輸入客戶、商品與數量
  ├─ validate_order(customer_id, product_id, order_qty)，L44–L71
  │    ├─ 檢查數量
  │    ├─ 讀取及檢查客戶狀態
  │    └─ 讀取及檢查商品狀態
  ├─ get_product_price(product_id)，L73–L78
  │    └─ 讀取商品單價
  ├─ 計算金額
  └─ 寫入訂單主檔、取得編號、寫入明細及更新客戶日期
       ├─ SQLCA.SQLCODE <> 0 → 原碼安排回滾
       └─ 其他分支 → 原碼安排提交
```

兩個顯式 `CALL` 的函式都在本檔定義，沒有顯式呼叫本檔未定義的自訂函式。資料表、畫面欄位配置與執行環境仍是外部依賴。

## 4. MAIN 處理流程

**範圍：L4–L42。** 此區塊協調使用者輸入、檢核、計價與資料寫入。

1. **宣告工作變數，L5–L11。** 包含訂單編號、輸入編號、數量、單價、金額與檢核訊息。
2. **顯示標題並取得輸入，L13–L14。** 顯示 `New Order Entry`，以 `INPUT BY NAME` 讀取三個輸入欄位。
3. **執行訂單檢核，L16–L17。** 將輸入傳給 `validate_order`，接收回傳值至 `validation_message`。
4. **處理拒絕結果，L19–L22。** 當 `validation_message <> "OK"` 成立時顯示該訊息，接著以 `RETURN` 提前離開主流程。原碼沒有另行判斷訊息為 NULL 的情況。
5. **讀取單價，L24–L25。** 呼叫 `get_product_price`，將回傳值接到 `unit_price`。本段沒有檢查查無價格、價格為 NULL 或負值。
6. **計算金額，L26。** 將 `unit_price * order_qty` 指派給 `order_total`。沒有其他加減項目或顯式捨入處理。
7. **開始交易，L28。** 執行 `BEGIN WORK`。先前的檢核與取價均位於這個交易區段之前。
8. **新增訂單主檔，L29，SQL-01。** 寫入客戶、當日日期、計算金額與狀態值 `"N"`。INSERT 欄位清單沒有 `order_id`。
9. **查詢訂單編號，L30，SQL-02。** 以 `MAX(order_id)` 的結果指派給程式變數 `order_id`。本報告不將此結果認定為可靠的「剛新增訂單編號」，原因見 Q-01、Q-02。
10. **新增一筆明細，L31，SQL-03。** 寫入取得的訂單編號、輸入商品、輸入數量及讀到的單價。
11. **更新客戶日期，L32，SQL-04。** 將 `last_order_date` 設為 `TODAY`。實際更新對象需確認同名識別字的繫結。
12. **選擇交易後續分支，L34–L41。** 若執行已到達 L34 且 `SQLCA.SQLCODE <> 0` 成立，執行回滾、顯示失敗訊息並 RETURN。否則執行提交，接著顯示 `Order created:` 與 `order_id`。本檔沒有於 COMMIT 之後另行檢查結果。

上述為原始碼排列的控制流程，不能據此保證任何 SQL 執行錯誤都會被 L34 捕捉，或所有路徑都可在特定 4GL 環境成功執行。

## 5. 各函式的用途與流程

### 5.1 validate_order

**範圍：L44–L71。** 檢查輸入數量，以及從資料庫讀到的客戶、商品狀態，回傳文字結果。

| 參數 | 程式宣告 | 呼叫端來源 | 在函式中的用途 |
| --- | --- | --- | --- |
| `p_customer_id` | `INTEGER`，L45 | `MAIN.customer_id` | SQL-05 的客戶查詢條件 |
| `p_product_id` | `INTEGER`，L46 | `MAIN.product_id` | SQL-06 的商品查詢條件 |
| `p_qty` | `INTEGER`，L47 | `MAIN.order_qty` | 與 0 比較，判斷是否拒絕 |

處理順序：

1. 將區域變數 `message_text` 設為 `"OK"`，L52。
2. 若 `p_qty <= 0`，直接回傳 `Quantity must be greater than zero`，L53–L55，後續 SQL 不再執行。
3. 以 `p_customer_id` 查詢 `customer.status`，結果放入 `customer_status`，L57。
4. 若 `customer_status IS NULL`，回傳 `Customer not found`，L58–L60。這個判斷檢查的是變數為 NULL，沒有直接檢查 SQL NOT FOUND 狀態，因此不能只憑這段判定資料庫一定沒有該筆客戶。
5. 若 `customer_status = "D"`，回傳 `Disabled customer cannot place orders`，L61–L63。
6. 以 `p_product_id` 查詢 `product.status`，結果放入 `product_status`，L65。
7. 若 `product_status <> "A"` 成立，回傳 `Product is unavailable`，L66–L68。此處沒有獨立的商品查無資料或 NULL 檢查。
8. 若沒有從前述分支返回，回傳 `message_text`，依本檔指派即為 `"OK"`，L70。

**回傳與副作用：** 回傳文字值，成功路徑由 `CHAR(120)` 區域變數回傳，拒絕路徑直接回傳文字常值。函式含兩處 SELECT，沒有資料庫寫入、畫面顯示或交易控制。它的回傳值由 MAIN 接收後決定是否繼續。

本函式通過代表沒有從已寫出的拒絕分支離開，不能擴大解釋為「所有訂單資料均已驗證有效」。

### 5.2 get_product_price

**範圍：L73–L78。** 依商品編號讀取單價，將查詢接收變數回傳。

| 項目 | 說明 |
| --- | --- |
| 參數 | `p_product_id`，宣告為 `INTEGER`，L74，來自 `MAIN.product_id` |
| 接收變數 | `result_price`，宣告為 `DECIMAL(12,2)`，L75 |
| 資料讀取 | SQL-07 從 `product.unit_price` 取值，L76 |
| 回傳 | `RETURN result_price`，L77，由 MAIN 的 `unit_price` 接收 |

函式沒有分支、迴圈或額外計算，未顯式處理查無資料、多筆結果、NULL 單價、SQL 錯誤或價格範圍。函式本身沒有寫入資料庫。

## 6. 商業規則與處理規則

| 編號 | 原始條件／動作 | 處理結果 | 可說明的意義與限制 | 證據 |
| --- | --- | --- | --- | --- |
| BR-01 | `p_qty <= 0` | 立即回傳數量錯誤訊息 | 明確拒絕小於等於零的數量，未見 NULL 或上限檢核 | L53–L55 |
| BR-02 | `customer_status IS NULL` | 回傳 `Customer not found` | 程式將 NULL 狀態視為此拒絕情況，不能等同已證明無客戶資料 | L57–L60 |
| BR-03 | `customer_status = "D"` | 拒絕下單 | 提示字串可支持「本流程將 D 當作停用狀態」的解釋，其他代碼意義未提供 | L61–L63 |
| BR-04 | `product_status <> "A"` | 條件成立時拒絕商品 | A 是本條件允許通過的明確值，NULL 行為不能視為已涵蓋 | L65–L68 |
| BR-05 | `validation_message <> "OK"` | 顯示訊息並 RETURN | MAIN 以這個比較決定拒絕，沒有另外處理 NULL 訊息 | L19–L22 |
| BR-06 | `unit_price * order_qty` | 指派給 `order_total` | 金額直接由單價乘數量得出，沒有可見的稅、折扣或運費項目 | L26 |
| BR-07 | 主檔 `status` 寫入 `"N"` | 設定新寫入資料的狀態值 | N 是確定的寫入值，「新增／新單」只是依用途推測 | L29 |
| BR-08 | `order_date`、`last_order_date` 使用 `TODAY` | 以當日值寫入 | 沒有讓使用者輸入訂購日期，時區及日切規則本檔未提供 | L29、L32 |
| BR-09 | 主檔、明細及客戶日期更新置於 BEGIN WORK 之後 | 原碼安排共同交易 | 交易支援、錯誤處理及提交結果仍須確認 | L28–L41 |
| BR-10 | 到達 L34 時 `SQLCA.SQLCODE <> 0` | 安排 ROLLBACK 與失敗訊息 | 不是逐一保存每條 SQL 結果，也沒有另外檢查受影響筆數 | L34–L37 |
| BR-11 | L34 比較未走回滾分支 | 安排 COMMIT 與成功訊息 | 執行至顯示訊息的確切條件仍受執行環境及 SQL 結果影響 | L38–L40 |

## 7. 完整 SQL 與交易控制

以下保留來源中的敘述文字與雙引號，不改寫條件、不補上參數前綴，也不將嵌入式 `INTO` 改成其他資料庫的語法。SQL 編號依原始檔出現順序編排。

沿預期成功路徑，實際呼叫順序為 **SQL-05 → SQL-06 → SQL-07 → BEGIN WORK → SQL-01 → SQL-02 → SQL-03 → SQL-04 → 交易分支**。任一步的返回或執行錯誤可能使後續不再執行。

### SQL-01：新增訂單主檔

來源：`MAIN`，L29。位於 BEGIN WORK 之後。

```sql
INSERT INTO orders (customer_id, order_date, total_amount, status) VALUES (customer_id, TODAY, order_total, "N")
```

| 目標欄位 | VALUES 中的來源 | 判讀 |
| --- | --- | --- |
| `orders.customer_id` | `customer_id` | 依 MAIN 上下文為輸入客戶編號，實際繫結需目標方言確認 |
| `orders.order_date` | `TODAY` | 當日值 |
| `orders.total_amount` | `order_total` | 由單價乘數量得到的程式變數 |
| `orders.status` | `"N"` | 固定狀態值 |

INSERT 沒有寫出 `order_id`，本檔未提供其型別、預設值或產生方式，不能假定為自動編號欄位。

### SQL-02：查詢最大訂單編號

來源：`MAIN`，L30。接收位置為程式變數 `order_id`。

```sql
SELECT MAX(order_id) INTO order_id FROM orders WHERE customer_id = customer_id
```

- 查詢欄位為 `orders.order_id`，使用 `MAX` 聚合。
- 兩側 `customer_id` 同名，無法僅憑本文確認篩選是否指向輸入客戶。
- `MAX` 的結果並非 INSERT 所回傳的識別值，本檔沒有建立可靠的兩者對應。
- 若其他工作同時新增訂單，即使篩選意圖正確，也不能只憑 MAX 保證取得本次新增的資料。
- 結果沒有另行檢查 NULL，後續直接用於 SQL-03。

### SQL-03：新增一筆訂單明細

來源：`MAIN`，L31。

```sql
INSERT INTO order_detail (order_id, product_id, quantity, unit_price) VALUES (order_id, product_id, order_qty, unit_price)
```

| 目標欄位 | 程式資料來源 |
| --- | --- |
| `order_detail.order_id` | SQL-02 的接收變數 `order_id` |
| `order_detail.product_id` | 使用者輸入的 `product_id` |
| `order_detail.quantity` | 使用者輸入的 `order_qty` |
| `order_detail.unit_price` | `get_product_price` 回傳後由 MAIN 接收的 `unit_price` |

本檔沒有包住此 INSERT 的明細迴圈，亦未見明細序號的顯式寫入。

### SQL-04：更新客戶最近訂購日期

來源：`MAIN`，L32。

```sql
UPDATE customer SET last_order_date = TODAY WHERE customer_id = customer_id
```

- 寫入 `customer.last_order_date`。
- 同名條件的兩側無法在本報告中直接認定為「資料表欄位對輸入變數」。
- 若兩側均解析為資料表欄位，條件便無法達到限制本次客戶的意圖，可能擴大更新範圍。
- 本檔沒有檢查受影響筆數是否恰為 1。

### SQL-05：讀取客戶狀態

來源：`validate_order`，L57。只有數量檢查未提前返回時才會執行。

```sql
SELECT status INTO customer_status FROM customer WHERE customer_id = p_customer_id
```

輸入為函式參數 `p_customer_id`，讀取 `customer.status`，結果放入區域變數 `customer_status`，接著用於 NULL 與 `"D"` 比較。未見 SQLCODE、回傳筆數或唯一性的顯式檢查。

### SQL-06：讀取商品狀態

來源：`validate_order`，L65。只有前述數量與客戶拒絕條件未提前返回時才會執行。

```sql
SELECT status INTO product_status FROM product WHERE product_id = p_product_id
```

輸入為函式參數 `p_product_id`，讀取 `product.status`，結果放入 `product_status`，供後續與 `"A"` 比較。本檔沒有另行檢查 NULL、查無資料或多筆結果。

### SQL-07：讀取商品單價

來源：`get_product_price`，L76。MAIN 的檢核訊息比較未提前返回後，才呼叫此函式。

```sql
SELECT unit_price INTO result_price FROM product WHERE product_id = p_product_id
```

輸入為函式參數 `p_product_id`，讀取 `product.unit_price` 至 `result_price`，再 RETURN 給 MAIN。接收變數為 `DECIMAL(12,2)`，這不是資料庫欄位型別的證明。

### 7.1 資料庫與交易敘述原文

資料庫宣告，L2：

```4gl
DATABASE legacy_orders
```

交易開始，L28：

```4gl
BEGIN WORK
```

L34–L41 的完整交易分支：

```4gl
    IF SQLCA.SQLCODE <> 0 THEN
        ROLLBACK WORK
        DISPLAY "Order creation failed"
        RETURN
    ELSE
        COMMIT WORK
        DISPLAY "Order created:", order_id
    END IF
```

## 8. 資料表與欄位說明

下表完整列出本檔靜態 SQL 可辨識的 15 個資料表欄位。**所有資料庫欄位的實際型別、長度、可空性、預設值、主外鍵、唯一性與約束均未提供。** 不能因為名稱相似或程式變數已有 DEFINE，就補成資料庫結構。

| 資料表．欄位 | 本檔使用方式 | 程式對應／值來源 | 可說明的意義 | 證據 |
| --- | --- | --- | --- | --- |
| `orders.order_id` | 讀取 MAX | 結果接到 `MAIN.order_id` | 依名稱與明細關聯用途推測為訂單識別碼，產生機制未知 | SQL-02、SQL-03 |
| `orders.customer_id` | INSERT 目標及查詢條件名稱 | MAIN 的同名輸入變數，WHERE 有歧義 | 依用途推測為訂單對應客戶，未證實為外鍵 | SQL-01、SQL-02 |
| `orders.order_date` | 寫入 | `TODAY` | 程式將當日值作為訂單日期，時區與業務日規則未知 | SQL-01 |
| `orders.total_amount` | 寫入 | `order_total` | 接收單價乘數量的結果，不足以判定是否含稅或其他費用 | SQL-01、L26 |
| `orders.status` | 寫入 | `"N"` | 固定初始狀態值，「新單」為依上下文推測 | SQL-01 |
| `order_detail.order_id` | 寫入 | `MAIN.order_id` | 用途上對應訂單主檔，但本檔未提供關聯約束 | SQL-03 |
| `order_detail.product_id` | 寫入 | 使用者輸入的 `product_id` | 依用途推測為明細所對應商品 | SQL-03 |
| `order_detail.quantity` | 寫入 | `order_qty` | 本次輸入數量，計量單位與允許上限未知 | SQL-03、L14 |
| `order_detail.unit_price` | 寫入 | MAIN 的 `unit_price` | 保存本次取價結果，幣別與價格制度未知 | SQL-03、SQL-07 |
| `customer.customer_id` | 查詢條件，另出現在 UPDATE 條件 | `p_customer_id`，以及 MAIN 的同名條件 | 依用途推測為客戶識別碼，唯一性未知 | SQL-04、SQL-05 |
| `customer.status` | 讀取 | `validate_order.customer_status` | 本流程以 D 拒絕下單，NULL 回傳找不到客戶提示，其他代碼意義未知 | SQL-05、L58–L63 |
| `customer.last_order_date` | 更新 | `TODAY` | 依命名推測為最近訂購日期，實際更新範圍待確認 | SQL-04 |
| `product.product_id` | 查詢條件 | 兩個函式中的 `p_product_id` | 依用途推測為商品識別碼，唯一性未知 | SQL-06、SQL-07 |
| `product.status` | 讀取 | `validate_order.product_status` | 用於與 A 比較，程式沒有提供完整狀態碼定義 | SQL-06、L66–L68 |
| `product.unit_price` | 讀取 | `get_product_price.result_price` | 商品計價資料的來源，幣別、含稅與歷史價格規則未知 | SQL-07 |

### 8.1 程式變數與參數

以下型別只來自本檔 DEFINE。

| 範圍 | 名稱 | 宣告型別 | 來源與用途 | 宣告行 |
| --- | --- | --- | --- | --- |
| MAIN | `order_id` | `INTEGER` | SQL-02 接收值，寫入明細並顯示 | L5 |
| MAIN | `customer_id` | `INTEGER` | 使用者輸入，傳入檢核及訂單寫入 | L6 |
| MAIN | `product_id` | `INTEGER` | 使用者輸入，傳入檢核、取價及明細寫入 | L7 |
| MAIN | `order_qty` | `INTEGER` | 使用者輸入，傳入數量檢核、金額計算及明細寫入 | L8 |
| MAIN | `unit_price` | `DECIMAL(12,2)` | 接收取價回傳值，參與金額計算並寫入明細 | L9 |
| MAIN | `order_total` | `DECIMAL(14,2)` | 單價乘數量的結果，寫入主檔 | L10 |
| MAIN | `validation_message` | `CHAR(120)` | 接收檢核文字，決定是否顯示並提前返回 | L11 |
| validate_order | `p_customer_id` | `INTEGER` | 參數，來自 MAIN 的客戶編號 | L45 |
| validate_order | `p_product_id` | `INTEGER` | 參數，來自 MAIN 的商品編號 | L46 |
| validate_order | `p_qty` | `INTEGER` | 參數，來自 MAIN 的數量 | L47 |
| validate_order | `customer_status` | `CHAR(1)` | 接收 customer.status，再進行判斷 | L48 |
| validate_order | `product_status` | `CHAR(1)` | 接收 product.status，再進行判斷 | L49 |
| validate_order | `message_text` | `CHAR(120)` | 設為 OK，作為函式末端回傳值 | L50 |
| get_product_price | `p_product_id` | `INTEGER` | 參數，來自 MAIN 的商品編號 | L74 |
| get_product_price | `result_price` | `DECIMAL(12,2)` | 接收 product.unit_price，隨後回傳 | L75 |

兩個函式的 `p_product_id` 是不同範圍內的參數，不能只因同名就合併成一個變數。本檔沒有 GLOBALS 區塊或 RECORD 宣告。

### 8.2 主要資料流

```text
MAIN.customer_id
  → validate_order.p_customer_id
  → SQL-05 的 customer.customer_id 條件
  → customer.status → customer_status → NULL / D 判斷

MAIN.product_id
  → validate_order.p_product_id
  → SQL-06 的 product.product_id 條件
  → product.status → product_status → 與 A 比較

MAIN.product_id
  → get_product_price.p_product_id
  → SQL-07 的 product.product_id 條件
  → product.unit_price → result_price → RETURN → MAIN.unit_price
  → 與 MAIN.order_qty 相乘 → MAIN.order_total → orders.total_amount

MAIN.order_qty → validate_order.p_qty → 與 0 比較
MAIN.order_qty → order_detail.quantity
MAIN.unit_price → order_detail.unit_price

MAX(orders.order_id) → MAIN.order_id → order_detail.order_id
                                 └→ 成功路徑顯示訊息
```

## 9. 路徑、交易與錯誤處理

| 路徑 | 進入條件 | 原碼後續動作 | 當時是否已執行 BEGIN WORK |
| --- | --- | --- | --- |
| 數量拒絕 | L53 條件成立 | 函式回傳訊息，MAIN 的比較成立後顯示並 RETURN | 否 |
| 客戶 NULL 狀態拒絕 | L58 條件成立 | 回傳找不到客戶訊息，交由 MAIN 處理 | 否 |
| 客戶停用拒絕 | L61 條件成立 | 回傳不能下單訊息，交由 MAIN 處理 | 否 |
| 商品狀態拒絕 | L66 條件成立 | 回傳商品無法使用訊息，交由 MAIN 處理 | 否 |
| 回滾分支 | 程式到達 L34，且比較成立 | ROLLBACK、失敗訊息、RETURN | 是 |
| 提交分支 | 程式到達 L34，且未走回滾分支 | COMMIT、顯示訂單編號 | 是 |

檢核與價格讀取位於交易開始之前。它們與資料寫入之間，是否可能因其他工作修改資料而改變有效性，需依實際交易與鎖定環境確認。

`SQLCA.SQLCODE` 用於描述 SQL 執行結果，不能充當整段交易內所有 SQL 結果的累積清單。本檔 L34 前最後一條明確 SQL 是 L32 的 UPDATE，亦未在每條 SQL 後保存結果。4GL 的 SQL 錯誤處理模式又可能先中止程式，因此不能保證任何錯誤都會到達這個回滾分支。此處的語言行為參考見第 11 節。

## 10. 待確認與值得核對的原碼行為

這些項目是單檔分析發現的疑點，不直接等同已確認缺陷，也不在本報告中改寫原碼。

| 編號 | 原碼證據 | 可能影響與待確認事項 |
| --- | --- | --- |
| Q-01 | L30、L32 的 `WHERE customer_id = customer_id` | 必須釐清欄位與程式變數的名稱繫結。不可直接將條件解釋為「只處理本次輸入客戶」，若均為欄位可能擴大讀取或更新範圍 |
| Q-02 | L29 未寫 order_id，L30 使用 MAX | 不清楚編號如何產生，也不能保證 MAX 對應本次 INSERT，需確認資料結構與併發行為 |
| Q-03 | L28–L40 沒有 WHENEVER ERROR 等顯式錯誤模式設定 | 不能保證 SQL 失敗後仍繼續到 L34，更不能保證本檔的回滾分支涵蓋所有失敗 |
| Q-04 | L34 只比較一次 SQLCA.SQLCODE | 未逐條確認 SQL 結果、未檢查 UPDATE 影響筆數，COMMIT 之後也沒有額外檢查 |
| Q-05 | L53、L66、L19 以一般比較處理可能為 NULL 的值 | 未顯式涵蓋 NULL 數量、NULL 商品狀態與 NULL 檢核訊息。標準 I4GL 的 NULL 比較不應當成一般真／假二值比較，實際方言仍須核對 |
| Q-06 | L57–L59 由 customer_status IS NULL 回傳找不到客戶 | 無法區別無資料與可空狀態欄位等情況，亦未直接檢查 SELECT 的 NOT FOUND 或錯誤結果 |
| Q-07 | L65、L76 的 SELECT 未檢查筆數及結果 | 商品不存在、重複資料、NULL 狀態／單價與 SQL 失敗時的後續行為未在本檔明確處理 |
| Q-08 | L26 的乘法與 L9–L10 的 DECIMAL 宣告 | 未提供金額上限、捨入、溢位、幣別與稅額規則，不能自行補入業務假設 |
| Q-09 | 檢核與取價位於 L28 之前 | 寫入前沒有再驗證狀態或價格是否改變，本檔無法說明一致性保證 |
| Q-10 | L14 使用 INPUT BY NAME，未見表單開啟或顯示敘述 | 所需的畫面、欄位配置與啟動環境未提供，不能宣稱此單檔可獨立執行 |

## 11. 分析完整度與證據界線

- 已逐段閱讀本檔 MAIN 與兩個 FUNCTION，涵蓋其中的 IF、CALL、RETURN、指派、畫面輸入輸出及交易控制。本檔沒有迴圈。
- 已完整列出 7 處靜態 SQL 原文及其執行位置，沒有自行修正同名條件或編號取得方式。本檔未見動態 SQL 組裝。
- 已列出所有可從這 7 處 SQL 辨識的資料表欄位，以及所有 DEFINE 參數與變數。
- 所有正式資料庫型別、鍵、約束、觸發器、預設值與業務狀態碼定義均未知。資料表及欄位名稱不構成這些定義的證明。
- 欄位意義優先描述「本程式如何使用它」，推測部分明確標示，不建立外部資料字典，也不延伸到其他程式。
- 沒有執行來源程式、SQL、資料庫異動或效能測試，也沒有確認 4GL 編譯與執行環境。
- 這份文件是預期報告內容範例，並未修改現有解析器、分析器、報告產生器或轉譯器。

### 11.1 人工評閱的語言參考

以下僅支持 NULL 與錯誤處理的語言風險解讀，不代表已確認本範例使用這些版本，也不替代來源證據。報告的產生與閱讀不要求連線開啟參考文件。

- 標準 I4GL 將 NULL 布林判斷視同未成立，因此一般比較式不能取代顯式 NULL 檢查。參考 [HCL Informix 4GL Concepts and Use，Decisions Based on NULL，書內頁碼 8-33](https://help.hcl-software.com/hclinformix/1410/pdf/4gl/HCL_4GL%20Concepts%20and%20Use%2C%20v7.51.pdf)。
- SQLCODE 在 SQL 執行後更新，0、100 與負值有不同意義。參考 [HCL Informix 4GL Reference Guide，Error Handling with SQLCA，書內頁碼 2-46](https://help.hcl-software.com/hclinformix/15.0.0/pdf/4gl/HCL_4GL%20Reference%20Manual.pdf)。
- 標準 4GL 預設會在執行錯誤時終止，繼續處理需有對應錯誤模式設定。參考 [HCL Informix 4GL By Example，Recovering from Runtime Errors，書內頁碼 72](https://help.hcl-software.com/hclinformix/15.0/pdf/4gl/HCL_4GL%20By%20Example%2C%20v7.51.pdf)。
