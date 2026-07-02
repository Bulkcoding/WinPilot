# WinPilot 개발 규칙

## DataGrid 스타일 규칙

앱 안의 모든 `DataGrid`는 `App.xaml`에 정의된 공용 스타일을 그대로 써야 한다.
새 그리드를 추가할 때 절대 자체 헤더/행/셀 스타일을 새로 만들지 말 것.

```xml
<DataGrid ...
          GridLinesVisibility="Horizontal"
          HorizontalGridLinesBrush="{DynamicResource BorderBrush}"
          Background="{DynamicResource SurfaceBrush}"
          RowBackground="{DynamicResource SurfaceBrush}"
          BorderThickness="0"
          ColumnHeaderStyle="{StaticResource DataGridHeaderStyle}"
          RowStyle="{StaticResource DataGridRowStyle}"
          CellStyle="{StaticResource DataGridCellStyle}">
```

### 규칙
- **행 배경은 통일한다. 얼터네이팅(줄무늬) 금지.** `AlternatingRowBackground`를 절대 설정하지 않는다.
  흰색/회색이 번갈아 나오는 지그재그 배경은 촌스럽다는 피드백으로 명시적으로 제거된 스타일이다.
  행 구분은 오직 `GridLinesVisibility="Horizontal"` + 얇은 `HorizontalGridLinesBrush`로만 한다.
- **행 높이는 `DataGridRowStyle`의 `MinHeight="38"`을 그대로 따른다.** `RowHeight`를 개별 그리드에
  직접 지정하지 않는다 (지정하면 공용 스타일을 무시하고 고정 높이가 되어버림).
- 헤더는 `AltRowBrush` 배경 + 굵은 글씨 + 하단 1px 구분선, 셀 폰트는 13px, 패딩은 헤더 10,10 / 셀 10,0.
- 선택(포커스) 시 배경은 `NavActiveBrush`, 글자는 `TextPrimaryBrush`로 밝게 — 기본 셀 스타일에 이미 포함됨.

### SelectionUnit="FullRow" 를 쓰는 그리드는 예외 처리가 필요하다
드래그로 여러 행을 한 번에 선택하게 하려면 `SelectionUnit="FullRow"` + `SelectionMode="Extended"`를 쓰는데,
이 모드에서는 `DataGridCell.IsSelected`가 발동하지 않는다 (선택 상태는 `DataGridRow` 쪽에서 처리됨).
그래서 `DataGridCellStyle`의 `IsSelected` 트리거는 무시되고, 대신 `DataGridRow`의 **기본 컨트롤 템플릿이
`SystemColors.HighlightBrushKey` / `ControlBrushKey`를 직접 참조해서 그린다.** 이 색을 못 바꾸면 드래그
선택 시 배경이 거의 까맣게 보인다.

`SelectionUnit="FullRow"`를 쓰는 그리드는 반드시 그 DataGrid의 `.Resources`에 아래 네 개를 로컬로
오버라이드해야 한다 (Uties `정리 결과` 그리드 참고):

```xml
<DataGrid.Resources>
    <SolidColorBrush x:Key="{x:Static SystemColors.HighlightBrushKey}" Color="#3A3A48"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.HighlightTextBrushKey}" Color="White"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.ControlBrushKey}" Color="#3A3A48"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.ControlTextBrushKey}" Color="White"/>
</DataGrid.Resources>
```

`SelectionUnit`을 기본값(Cell 단위)으로 쓰는 일반 그리드는 이 오버라이드가 필요 없다 — 공용
`DataGridCellStyle`의 `IsSelected` 트리거가 그대로 잘 동작한다.

### 적용 대상
`EventViewerView`, `ProcessManagerView`(4개 그리드), `UtilitiesView`(정리 결과) 모두 이 규칙으로
통일되어 있다. 새 그리드를 추가할 때도 이 표를 그대로 따를 것.

## GridSplitter 스타일 규칙

패널 크기 조절용 `GridSplitter`는 항상 `Background="Transparent"`로 둔다. 색이 있는 브러시를
넣으면 화면에 굵은 막대가 그대로 보여서 지저분하다. `Transparent`여도 히트테스트(드래그)는
정상 동작하므로 기능은 그대로 유지되면서 시각적으로는 안 보이게 된다.

```xml
<GridSplitter Width="6" HorizontalAlignment="Stretch" VerticalAlignment="Stretch"
              Background="Transparent"
              ResizeBehavior="PreviousAndNext" ShowsPreview="True"/>
```
