# Requirements Document

## Introduction

不思議のダンジョン系（トルネコ・シレン）のローグライクマップ生成システムを、区画割法（Grid Split）アルゴリズムを用いて実装する。2Dタイルグリッドとしてダンジョンマップを生成し、それを3Dで描画する。部屋の数・サイズ・通路パターン・特殊部屋モードなど、パラメータを調整しながら反復的に開発できるテストプロジェクトとして構築する。

## Glossary

- **Map_Generator**: 設定パラメータから2Dダンジョンマップを生成する中核システム
- **Map_Grid**: ダンジョンレイアウトを表すタイル値の2次元配列
- **Partition**: 部屋配置のコンテナとして使用されるマップ領域の矩形区画
- **Room**: Partition内に配置される床タイルの矩形領域
- **Corridor**: 隣接するPartitionまたはRoomを接続する1タイル幅の通路
- **Room_Entrance**: RoomとCorridorの境界を示す専用タイル
- **Tile**: Map_Grid内の1セル。TileType値で識別される
- **TileType**: タイル識別子の列挙型（Wall, Floor, Corridor, Room_Entrance, Stairs_Down）
- **Map_Renderer**: Map_Gridを3Dジオメトリに変換するシステム
- **Auto_Tile_Processor**: 周囲タイルのコンテキストに基づいて配置する3D壁バリアントを決定するサブシステム
- **Generation_Config**: マップ寸法、グリッド分割数、部屋サイズ制約、通路間引き確率を制御するパラメータオブジェクト

## Requirements

### Requirement 1: グリッド区画分割

**User Story:** 開発者として、マップ領域を設定可能なグリッド状の区画に分割したい。不思議のダンジョン特有の構造化されたレイアウトで部屋を配置するためである。

#### Acceptance Criteria

1. WHEN Map_Generatorがグリッド寸法（行数と列数）を指定するGeneration_Configを受信した場合, THE Map_Generator SHALL マップ領域を行数×列数に等しいPartitionのグリッドに等分割し、すべてのPartitionがマップ領域全体を隙間なく重複なくカバーすることを保証する
2. THE Map_Generator SHALL 各Partitionの最小幅を9タイル、最小高さを9タイルとして確保する
3. IF マップ寸法が最小Partitionサイズ（9タイル）で要求されたグリッド分割数をサポートできない場合, THEN THE Map_Generator SHALL 行数と列数をそれぞれ独立に、最小Partitionサイズ制約を満たす最大値まで削減する
4. WHEN マップ幅または高さが分割数で割り切れない場合, THE Map_Generator SHALL 余剰タイルをグリッド端部（最終列または最終行）のPartitionに加算して割り当てる

### Requirement 2: 区画内の部屋生成

**User Story:** 開発者として、区画内にランダムサイズの矩形部屋を配置したい。生成されるダンジョンごとに異なる部屋レイアウトを実現するためである。

#### Acceptance Criteria

1. WHEN 部屋を生成する際, THE Map_Generator SHALL 各Partitionに最大1つのRoomを配置する
2. THE Map_Generator SHALL 各Roomの幅と高さをGeneration_Configの最小部屋サイズ（下限5タイル）以上、かつGeneration_Configの最大部屋サイズとPartition寸法から両辺のマージンを差し引いた値のうち小さい方以下として生成する
3. THE Map_Generator SHALL Room境界とPartition境界の間に全辺で最低1タイルの壁マージンを確保する
4. THE Map_Generator SHALL Roomをマージン制約を満たすPartition内の有効領域内でランダムな位置に配置する
5. WHEN Generation_Configの空き区画確率によりPartitionが空と指定された場合, THE Map_Generator SHALL そのPartitionにRoomを配置しない
6. THE Map_Generator SHALL 生成されるすべてのMap_Gridに最低2つのRoomが存在することを保証する
7. IF 空き区画確率の適用により生成されるRoom数が2未満となる場合, THEN THE Map_Generator SHALL 最低2つのRoomが確保されるまで空きPartition指定を無視してRoomを配置する

### Requirement 3: 通路接続

**User Story:** 開発者として、隣接する部屋を通路で接続したい。プレイヤーが部屋間を移動できるようにするためである。

#### Acceptance Criteria

1. WHEN グリッド上で水平方向または垂直方向に隣接する2つのPartitionが共にRoomを含む場合, THE Map_Generator SHALL 2つのRoomを接続する1タイル幅のCorridorを生成する
2. THE Map_Generator SHALL Corridorの起点を部屋の壁面のうち角タイル（壁面の両端セル）を除いた位置から選択し、壁面に対して垂直方向に延伸させる
3. WHEN 2つのRoomを接続する際, THE Map_Generator SHALL L字型（1回の直角曲がり）または直線のパスでCorridorをルーティングする
4. WHEN Generation_Configが0.0より大きく1.0以下の通路間引き確率を指定した場合, THE Map_Generator SHALL 各隣接Room間のCorridor生成判定時にその確率で当該Corridorをランダムに省略する
5. WHEN 通路間引きにより1つも通路接続を持たない孤立Roomが発生した場合, THE Map_Generator SHALL そのRoomのメタデータにhidden_roomフラグをtrueに設定し、隠し部屋として分類する
6. THE Map_Generator SHALL Corridor経路がRoom領域を横断しないようルーティングする

### Requirement 4: 部屋入口タイルのマーキング

**User Story:** 開発者として、部屋と通路の境界を専用タイルタイプで明示的にマークしたい。ゲームプレイシステムが部屋への出入りイベントを検知するためである。

#### Acceptance Criteria

1. WHEN CorridorがRoom壁面を貫通して接続する場合, THE Map_Generator SHALL そのRoom壁面上の貫通位置にあるWallタイルをRoom_Entranceタイルに置換する
2. THE Map_Generator SHALL 各Corridor-Room接続ごとに正確に1タイルのRoom_Entranceを、Corridorが接続するRoomの壁面セル位置にのみ配置する
3. THE Map_Generator SHALL すべての非隠しRoomに最低1つのRoom_Entranceタイルが存在することを保証する
4. THE Map_Generator SHALL Room_Entranceタイルを通行可能タイルとして扱い、CorridorからRoom内Floorへの移動経路を形成する

### Requirement 5: マップグリッド出力形式

**User Story:** 開発者として、生成されたマップをタイプ付きタイルの2次元配列として出力したい。描画システムとゲームプレイシステムが統一的に消費できるようにするためである。

#### Acceptance Criteria

1. THE Map_Generator SHALL Generation_Configのマップ幅と高さに一致する寸法のTileType値2次元配列としてMap_Gridを出力する
2. THE Map_Generator SHALL 部屋と通路の配置前にすべてのタイルをWallとして初期化する
3. THE Map_Generator SHALL 生成されたRoom内のすべてのセルにFloorタイルを割り当てる
4. THE Map_Generator SHALL Room外のすべての通路セルにCorridorタイルを割り当てる
5. THE Map_Generator SHALL すべての部屋-通路境界セルにRoom_Entranceタイルを割り当て、同セルに既に割り当てられたFloorまたはCorridorタイルを上書きする
6. WHEN 生成が完了した場合, THE Map_Generator SHALL 最低1つのCorridor接続を持つRoom内のFloorタイルからランダムに1セルを選択し、そのセルのTileTypeをStairs_Downに置換する
7. THE Map_Generator SHALL 各セルに正確に1つのTileType値を保持し、タイル割り当て優先順位をWall → Floor → Corridor → Room_Entrance → Stairs_Downの順で後続の割り当てが前の値を上書きするものとする

### Requirement 6: 特殊部屋モード

**User Story:** 開発者として、モンスターハウスと大部屋モードをサポートしたい。不思議のダンジョンの代表的な特殊遭遇を再現するためである。

#### Acceptance Criteria

1. WHEN Generation_Configがモンスターハウス生成を有効にした場合, THE Map_Generator SHALL 各非隠しRoomに対してGeneration_Configのモンスターハウス確率で判定を行い、1つ以上最大3つまでのRoomをRoomメタデータにMonsterHouseフラグを設定してモンスターハウスRoomとして指定する
2. WHEN Generation_Configが大部屋モードを有効にした場合, THE Map_Generator SHALL 区画分割をスキップし、1タイルの壁ボーダーを除くマップ全域に及ぶ単一Roomを生成し、そのRoom内にStairs_Downタイルを1つ配置する
3. WHEN Roomがモンスターハウスとして指定された場合, THE Map_Generator SHALL そのRoomのメタデータに通常Roomの3倍のアイテム密度乗数と通常Roomの3倍のモンスター密度乗数を記録する
4. IF 大部屋モードが有効かつモンスターハウス生成も有効の場合, THEN THE Map_Generator SHALL 大部屋Room自体にMonsterHouseフラグを設定する
5. WHEN 大部屋モードのマップを生成する場合, THE Map_Generator SHALL Corridor及びRoom_Entranceタイルを生成せず、単一RoomのFloorタイルのみで構成されるMap_Gridを出力する

### Requirement 7: 3Dマップ描画

**User Story:** 開発者として、2DマップグリッドをD3Dジオメトリとして描画したい。生成結果を視覚的に確認し、反復改善するためである。

#### Acceptance Criteria

1. WHEN Map_GridがMap_Rendererに提供された場合, THE Map_Renderer SHALL 各タイル位置をグリッド座標(x, y)からワールド座標(x × タイルサイズ, y × タイルサイズ)に変換し、TileTypeに対応する3Dアセットを配置する
2. THE Map_Renderer SHALL Wallタイルに壁アセットを、Floor・Corridor・Room_Entranceタイルに床アセットを、Stairs_Downタイルに床アセットと階段アセットの両方を配置する
3. WHEN 壁アセットを配置する際, THE Auto_Tile_Processor SHALL 周囲8タイルを検査し、隣接する非Wallタイルの方向パターンに基づいて壁バリアント（直線壁、内角、外角）を決定する
4. IF 壁タイルがマップ境界に隣接する場合, THEN THE Auto_Tile_Processor SHALL 境界外を仮想Wallタイルとして扱い壁バリアントを決定する
5. WHEN Room_Entranceタイルを配置する際, THE Map_Renderer SHALL 床アセットと共にタイルサイズと同一寸法の透明トリガーコライダーを配置する

### Requirement 8: 生成設定とチューニング

**User Story:** 開発者として、設定可能な生成パラメータを公開したい。コード変更なしでダンジョン特性を反復調整するためである。

#### Acceptance Criteria

1. THE Generation_Config SHALL 以下のパラメータを有効範囲と共に公開する：マップ幅（20～200タイル）、マップ高さ（20～200タイル）、グリッド行数（1～10）、グリッド列数（1～10）、最小部屋幅（5～50タイル）、最小部屋高さ（5～50タイル）、最大部屋幅（最小部屋幅～Partition幅-2タイル）、最大部屋高さ（最小部屋高さ～Partition高さ-2タイル）、空き区画確率（0.0～1.0）、通路間引き確率（0.0～1.0）、モンスターハウス確率（0.0～1.0）
2. IF Generation_Configパラメータが有効範囲外である場合, THEN THE Map_Generator SHALL パラメータを最も近い有効な境界値にクランプして生成を続行する
3. THE Map_Generator SHALL Generation_Config内のシード値（0～4294967295の整数）を受け入れ、同一シードに対して決定論的なマップ出力を生成する
4. IF Generation_Configにシード値が指定されていない場合, THEN THE Map_Generator SHALL ランダムなシード値を自動生成し、生成結果と共にそのシード値を出力する

### Requirement 9: 階段・スポーン配置制約

**User Story:** 開発者として、階段とスポーン地点の配置ルールを設けたい。生成されたマップで不公平な初期状態を防ぐためである。

#### Acceptance Criteria

1. THE Map_Generator SHALL Stairs_Downタイルを最低1つのCorridor接続を持つRoom内にのみ配置する
2. WHEN プレイヤースポーン位置を配置する際, THE Map_Generator SHALL Stairs_Downタイルを含むRoomとは異なる、最低1つのCorridor接続を持つ非隠しRoom内のFloorタイルを正確に1つ選択する
3. WHEN 初期モンスタースポーン位置を配置する際, THE Map_Generator SHALL プレイヤースポーン位置を含むRoom以外の非隠しRoomそれぞれに最低1体ずつモンスターを配置し、合計配置数をGeneration_Configで指定されたモンスター初期配置数とする
4. IF プレイヤースポーン配置条件を満たすRoom（Stairs_Down Roomとは異なり、最低1つのCorridor接続を持つ非隠しRoom）が存在しない場合, THEN THE Map_Generator SHALL 生成失敗として新しいランダムステートで再生成する
5. WHEN 初期モンスター配置数が対象Room数を超過する場合, THE Map_Generator SHALL 各対象Roomに最低1体を配置した後、残りをランダムに対象Room間へ割り振る

### Requirement 10: マップ接続性検証

**User Story:** 開発者として、隠し部屋以外のすべての部屋が到達可能であることを保証したい。生成されたマップが常にクリア可能であるためである。

#### Acceptance Criteria

1. WHEN 生成が完了した場合, THE Map_Generator SHALL プレイヤースポーンRoomの任意のFloorタイルを起点として4方向隣接（上下左右）によるFloor・Corridor・Room_Entranceタイルの探索を実行し、すべての非隠しRoomが少なくとも1タイル到達可能であることを検証する
2. IF 接続性検証が失敗した場合, THEN THE Map_Generator SHALL 新しいランダムステートを使用してマップ全体を区画分割から再生成する
3. IF 再生成試行回数が10回を超過した場合, THEN THE Map_Generator SHALL 生成処理を中止し、生成失敗を示す結果（成功フラグfalseおよび失敗理由を含む）を呼び出し元に返却する
