# Implementation Plan: ローグライクマップ生成

## Overview

区画割法（Grid Split）アルゴリズムによるローグライクダンジョンマップ生成システムをC#/.NET 10で実装する。生成ロジック（Map Generator Core）、データ層（MapGrid/メタデータ）、描画層（Silk.NET/OpenGL）の3層に分離し、xUnit + FsCheckによるプロパティベーステストで正当性を保証する。

## Tasks

- [x] 1. プロジェクト構造とコアデータモデルのセットアップ
  - [x] 1.1 プロジェクト構造の作成
    - `FF11Dungeon.MapGen/`（生成ロジック用クラスライブラリ）プロジェクトを作成
    - `FF11Dungeon.MapGen.Tests/`（テスト用）プロジェクトを作成
    - xUnit、FsCheck、FsCheck.Xunit パッケージ参照を追加
    - ソリューションファイルに両プロジェクトを追加
    - _Requirements: 全体基盤_

  - [x] 1.2 コアデータモデルの実装
    - `TileType` 列挙型（Wall=0, Floor=1, Corridor=2, RoomEntrance=3, StairsDown=4）を実装
    - `Vector2Int` record struct（四方向・八方向定数、加算演算子）を実装
    - `MapGrid` クラス（2次元配列、InBounds、IsPassable、GetTileOrWall）を実装
    - `Partition` struct、`PartitionGrid` クラスを実装
    - `Room` struct、`RoomMetadata` クラスを実装
    - `Corridor` struct を実装
    - `GenerationConfig` クラス（全パラメータ、デフォルト値）を実装
    - `GenerationResult` クラスを実装
    - _Requirements: 5.1, 5.2, 8.1_

  - [x]* 1.3 コアデータモデルのユニットテスト
    - MapGrid の InBounds/IsPassable/GetTileOrWall の境界値テスト
    - TileType の値の一致テスト
    - Vector2Int の演算子テスト
    - _Requirements: 5.1, 5.2_

- [x] 2. 区画分割（PartitionSplitter）の実装
  - [x] 2.1 PartitionSplitter の実装
    - マップ領域を rows×cols のグリッドに等分割するロジックを実装
    - 最小Partition寸法（9タイル）を満たせない場合の行数/列数自動削減を実装
    - 割り切れない余剰タイルをグリッド端部（最終行・最終列）のPartitionに加算する処理を実装
    - _Requirements: 1.1, 1.2, 1.3, 1.4_

  - [x]* 2.2 Property 1: 区画分割によるマップ完全被覆のプロパティテスト
    - **Property 1: 区画分割によるマップ完全被覆**
    - すべてのPartitionの合計面積がwidth×heightと一致し、重複なし・隙間なしを検証
    - FsCheck の GenValidConfig() カスタムジェネレーターを作成
    - **Validates: Requirements 1.1, 1.4**

  - [x]* 2.3 Property 2: 最小Partition寸法保証のプロパティテスト
    - **Property 2: 最小Partition寸法保証**
    - すべてのPartitionが幅9以上・高さ9以上であること、要求を満たせない場合の自動削減を検証
    - **Validates: Requirements 1.2, 1.3**

- [x] 3. 部屋生成（RoomGenerator）の実装
  - [x] 3.1 RoomGenerator の実装
    - 各Partitionに最大1つのRoomを生成するロジックを実装
    - 部屋サイズ制約（最小/最大、Partition寸法-マージン）の適用を実装
    - Partition境界から全辺1タイルのマージン確保を実装
    - 空き区画確率に基づくスキップ処理を実装
    - 最低2部屋保証ロジック（空き区画指定の無視）を実装
    - MapGrid へのFloorタイル書き込みを実装
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 5.3_

  - [x]* 3.2 Property 3: 部屋寸法・マージン制約のプロパティテスト
    - **Property 3: 部屋寸法・マージン制約**
    - 全RoomがPartition境界から1タイル以上のマージンを持ち、サイズが設定範囲内であることを検証
    - **Validates: Requirements 2.2, 2.3**

  - [x]* 3.3 Property 4: 最低部屋数保証のプロパティテスト
    - **Property 4: 最低部屋数保証**
    - 空き区画確率0.0～1.0のいずれでも最低2部屋が存在することを検証
    - **Validates: Requirements 2.6, 2.7**

- [x] 4. 通路接続（CorridorConnector）の実装
  - [x] 4.1 CorridorConnector の実装
    - 隣接Partition間のRoom同士を接続するCorridorの生成を実装
    - Corridor起点のRoom壁面選択（角タイル除外）ロジックを実装
    - L字型または直線のパスルーティングを実装
    - 通路がRoom領域を横断しない制約チェックを実装
    - 通路間引き確率に基づくCorridor省略を実装
    - 孤立Room（接続0）へのhidden_roomフラグ設定を実装
    - MapGrid へのCorridorタイル書き込みを実装
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 5.4_

  - [x]* 4.2 Property 5: 通路の構造的整合性のプロパティテスト
    - **Property 5: 通路の構造的整合性**
    - 直線またはL字型、起点が角タイル以外、Room領域と重複しないことを検証
    - **Validates: Requirements 3.2, 3.3, 3.6**

  - [x]* 4.3 Property 6: 通路間引きなし時の完全接続のプロパティテスト
    - **Property 6: 通路間引きなし時の完全接続**
    - CorridorPruneChance=0.0で隣接Room付きPartitionペア全てにCorridorが存在することを検証
    - **Validates: Requirements 3.1**

  - [x]* 4.4 Property 7: 隠し部屋フラグの整合性のプロパティテスト
    - **Property 7: 隠し部屋フラグの整合性**
    - Corridor接続0本のRoomはIsHiddenRoom=true、1本以上はfalseを検証
    - **Validates: Requirements 3.5**

- [x] 5. 部屋入口マーキングとタイル割り当ての実装
  - [x] 5.1 部屋入口（Room_Entrance）マーキングの実装
    - CorridorがRoom壁面を貫通する位置のWallタイルをRoom_Entranceに置換するロジックを実装
    - 各Corridor-Room接続ごとに正確に1タイルのRoom_Entrance配置を実装
    - Room_Entranceタイルの通行可能判定を実装
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 5.5_

  - [x]* 5.2 Property 8: 部屋入口タイルの正当性のプロパティテスト
    - **Property 8: 部屋入口タイルの正当性**
    - 各接続に正確に1つのRoom_Entrance、非隠しRoomに最低1つのRoom_Entranceを検証
    - **Validates: Requirements 4.1, 4.2, 4.3**

  - [x]* 5.3 Property 9: タイルタイプ整合性のプロパティテスト
    - **Property 9: タイルタイプ整合性**
    - Room内セルがFloor/Room_Entrance/StairsDown、通路セルがCorridor、グリッド寸法一致、StairsDown正確に1つを検証
    - **Validates: Requirements 5.1, 5.3, 5.6**

- [x] 6. Checkpoint - 生成コアの検証
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Generation_Config の検証とクランプの実装
  - [x] 7.1 パラメータクランプロジックの実装
    - GenerationConfig の各パラメータに対する有効範囲チェックとクランプ処理を実装
    - MinRoomSize > MaxRoomSize の場合の修正ロジックを実装
    - _Requirements: 8.1, 8.2_

  - [x]* 7.2 Property 10: パラメータクランプの正当性のプロパティテスト
    - **Property 10: パラメータクランプの正当性**
    - 範囲外の値が最も近い境界値にクランプされることを検証
    - GenOutOfRangeConfig() カスタムジェネレーターを使用
    - **Validates: Requirements 8.1, 8.2**

  - [x] 7.3 シード決定論性の実装
    - Generation_Config のシード値に基づく Random 初期化を実装
    - シード未指定時の自動生成と出力を実装
    - _Requirements: 8.3, 8.4_

  - [x]* 7.4 Property 11: シード決定論性のプロパティテスト
    - **Property 11: シード決定論性**
    - 同一Config・同一シードで2回生成した結果が完全一致することを検証
    - **Validates: Requirements 8.3**

- [x] 8. スポーン配置と接続性検証の実装
  - [x] 8.1 SpawnPlacer の実装
    - Stairs_Downタイルの配置（Corridor接続1以上のRoom内Floor）を実装
    - プレイヤースポーン位置の選択（Stairs Room以外の非隠しRoom）を実装
    - 初期モンスタースポーンの配置（プレイヤーRoom以外の各非隠しRoomに最低1体、合計をInitialMonsterCountに）を実装
    - スポーン配置失敗時の再生成トリガーを実装
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 5.6_

  - [x] 8.2 ConnectivityValidator の実装
    - プレイヤースポーンRoomのFloorタイルから4方向BFS/DFSによる到達性検証を実装
    - すべての非隠しRoomの少なくとも1タイルへの到達を確認するロジックを実装
    - _Requirements: 10.1_

  - [x] 8.3 再生成ループの実装
    - 接続性検証失敗時の新ランダムステートでの再生成を実装
    - 再生成試行回数10回超過時のGenerationResult.Success=false返却を実装
    - _Requirements: 10.2, 10.3_

  - [x]* 8.4 Property 12: スポーン配置制約のプロパティテスト
    - **Property 12: スポーン配置制約**
    - StairsDownがCorridor接続Room内、プレイヤースポーンが別の非隠しRoom内を検証
    - **Validates: Requirements 9.1, 9.2**

  - [x]* 8.5 Property 13: モンスター初期配置の分散のプロパティテスト
    - **Property 13: モンスター初期配置の分散**
    - プレイヤーRoom以外の各非隠しRoomに最低1体、合計がInitialMonsterCountと一致を検証
    - **Validates: Requirements 9.3, 9.5**

  - [x]* 8.6 Property 14: マップ接続性保証のプロパティテスト
    - **Property 14: マップ接続性保証**
    - プレイヤースポーンから4方向探索ですべての非隠しRoomに到達可能を検証
    - **Validates: Requirements 10.1**

- [x] 9. 特殊部屋モードの実装
  - [x] 9.1 モンスターハウス機能の実装
    - 各非隠しRoomに対するモンスターハウス判定（確率ベース、1～3部屋制限）を実装
    - MonsterHouseフラグ設定、ItemDensityMultiplier=3.0、MonsterDensityMultiplier=3.0の設定を実装
    - _Requirements: 6.1, 6.3_

  - [x] 9.2 大部屋モードの実装
    - BigRoomMode=true時の区画分割スキップと単一Room生成を実装
    - 1タイル壁ボーダー + マップ全域Floorの生成を実装
    - Corridor・Room_Entrance非生成の保証を実装
    - BigRoomMode + MonsterHouseEnabled時のフラグ設定を実装
    - _Requirements: 6.2, 6.4, 6.5_

  - [x]* 9.3 Property 15: モンスターハウス数の制約のプロパティテスト
    - **Property 15: モンスターハウス数の制約**
    - モンスターハウス数が1～3、密度乗数が3.0であることを検証
    - **Validates: Requirements 6.1, 6.3**

  - [x]* 9.4 Property 16: 大部屋モードのタイル制約のプロパティテスト
    - **Property 16: 大部屋モードのタイル制約**
    - Corridor/Room_Entranceなし、壁ボーダー以外が全てFloor/StairsDownを検証
    - **Validates: Requirements 6.2, 6.5**

- [x] 10. Checkpoint - 生成ロジック全体の検証
  - Ensure all tests pass, ask the user if questions arise.

- [x] 11. MapGenerator ファサードの統合
  - [x] 11.1 MapGenerator クラスの実装
    - Generate(config) メソッドの実装（全パイプラインの統合）
    - Config検証→グリッド初期化→区画分割→部屋生成→通路接続→入口マーキング→スポーン配置→接続性検証のパイプライン統合
    - GenerationResult の構築と返却を実装
    - _Requirements: 全体統合_

  - [x]* 11.2 統合テスト
    - デフォルト設定での正常生成テスト
    - 大部屋モード + モンスターハウスの組み合わせテスト
    - 再生成10回超過時の失敗テスト
    - シード未指定時の自動生成テスト
    - _Requirements: 8.3, 8.4, 10.2, 10.3_

- [x] 12. 3Dマップ描画（MapRenderer）の実装
  - [x] 12.1 MapRenderer の基本構造とメッシュ構築
    - Silk.NET / OpenGL 3.3 Core Profile の初期化を実装
    - タイル座標からワールド座標への変換（x × tileSize, y × tileSize）を実装
    - TileTypeに応じた3Dアセット配置（Wall→壁、Floor/Corridor/RoomEntrance→床、StairsDown→床+階段）を実装
    - Room_Entrance位置への透明トリガーコライダー配置を実装
    - BuildMesh / Render メソッドの実装
    - _Requirements: 7.1, 7.2, 7.5_

  - [x] 12.2 AutoTileProcessor の実装
    - 周囲8タイル検査による壁バリアント（Straight, InnerCorner, OuterCorner, End, None）判定ロジックを実装
    - マップ境界外を仮想Wallとして扱う処理を実装
    - WallVariant 列挙型に基づくジオメトリ選択を実装
    - _Requirements: 7.3, 7.4_

  - [x]* 12.3 Property 17: オートタイル壁バリアント決定の整合性のプロパティテスト
    - **Property 17: オートタイル壁バリアント決定の整合性**
    - 同一周囲パターンに対し常に同一バリアント、境界外は仮想Wallとして扱うことを検証
    - GenWallNeighborPattern() カスタムジェネレーターを使用
    - **Validates: Requirements 7.3, 7.4**

- [x] 13. Final checkpoint - 全体の検証
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- 生成ロジック（タスク1～11）は描画（タスク12）に依存しないため、独立してテスト可能
- FsCheck カスタムジェネレーター（GenValidConfig, GenOutOfRangeConfig, GenWallNeighborPattern）はテストプロジェクト内の共通ユーティリティとして実装する

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2"] },
    { "id": 2, "tasks": ["1.3", "2.1", "7.1"] },
    { "id": 3, "tasks": ["2.2", "2.3", "3.1", "7.2", "7.3"] },
    { "id": 4, "tasks": ["3.2", "3.3", "4.1", "7.4"] },
    { "id": 5, "tasks": ["4.2", "4.3", "4.4", "5.1"] },
    { "id": 6, "tasks": ["5.2", "5.3", "9.1", "9.2"] },
    { "id": 7, "tasks": ["8.1", "9.3", "9.4"] },
    { "id": 8, "tasks": ["8.2", "8.3", "8.4", "8.5"] },
    { "id": 9, "tasks": ["8.6", "11.1"] },
    { "id": 10, "tasks": ["11.2", "12.1"] },
    { "id": 11, "tasks": ["12.2"] },
    { "id": 12, "tasks": ["12.3"] }
  ]
}
```
