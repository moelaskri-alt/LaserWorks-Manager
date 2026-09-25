"""Generates consistent Avalonia list views (toolbar + grid + pager) from a compact spec."""
import sys, json, os

CONV = {"money": "{x:Static l:Conv.Money}", "num": "{x:Static l:Conv.Number}", "date": "{x:Static l:Conv.Date}", "enum": "{x:Static l:Conv.Enum}",
        "pct": "{x:Static l:Conv.Percent}", "dt": "{x:Static l:Conv.DateTimeShort}"}

def column(c):
    header, path = c[0], c[1]
    conv = c[2] if len(c) > 2 else None
    width = c[3] if len(c) > 3 else "*"
    sort = c[4] if len(c) > 4 else None
    if width.endswith("*"):
        k = float(width[:-1] or 1)
        width = f'{width}" MinWidth="{int(min(160, max(80, 70 * k)))}'
    else:
        width = f'{width}" MinWidth="{width}'
    if conv == "bool":
        return f'        <DataGridCheckBoxColumn Header="{{l:T {header}}}" Binding="{{Binding {path}}}" Width="{width}" />'
    b = f"{{Binding {path}, Converter={CONV[conv]}}}" if conv else f"{{Binding {path}}}"
    s = f' SortMemberPath="{sort}"' if sort else ""
    return f'        <DataGridTextColumn Header="{{l:T {header}}}" Binding="{b}"{s} Width="{width}" />'

def action(a):
    label, cmd = a[0], a[1]
    needs_sel = a[2] if len(a) > 2 else False
    primary = a[3] if len(a) > 3 else False
    extra = a[4] if len(a) > 4 else ""
    cls = ' Classes="primary"' if primary else ""
    sel = ' CommandParameter="{Binding Selected}" IsEnabled="{Binding Selected, Converter={x:Static l:Conv.NotNull}}"' if needs_sel else ""
    return f'          <Button{cls} Content="{{l:T {label}}}" Command="{{Binding {cmd}}}"{sel}{extra} />'

def gen(spec):
    name, vm = spec["name"], spec["vm"]
    cols = "\n".join(column(c) for c in spec["columns"])
    acts = "\n".join(action(a) for a in spec.get("actions", []))
    filters = spec.get("filters", "")
    open_cmd = spec.get("open")
    sort = ' l:GridBehaviors.ServerSort="True"' if spec.get("serverSort") else ""
    opn = f' l:GridBehaviors.OpenCommand="{{Binding {open_cmd}}}"' if open_cmd else ""
    rows = ' l:GridBehaviors.RowClasses="True"' if spec.get("rowClasses") else ""
    hint = f'\n    <TextBlock DockPanel.Dock="Top" Classes="muted wrap" Text="{{l:T {spec["hint"]}}}" Margin="0,0,0,8" />' if spec.get("hint") else ""
    return f'''<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:LaserWorks.Desktop.ViewModels"
             xmlns:c="using:LaserWorks.Desktop.Controls"
             xmlns:l="using:LaserWorks.Desktop"
             x:Class="LaserWorks.Desktop.Views.{name}"
             x:DataType="vm:{vm}">
  <DockPanel>
    <c:ListToolbar DockPanel.Dock="Top" Grid="{{Binding #grid}}" Margin="0,0,0,10">
      <c:ListToolbar.Filters>
        <StackPanel Orientation="Horizontal" Spacing="8">
{filters}
        </StackPanel>
      </c:ListToolbar.Filters>
      <c:ListToolbar.Actions>
        <StackPanel Orientation="Horizontal" Spacing="6">
{acts}
        </StackPanel>
      </c:ListToolbar.Actions>
    </c:ListToolbar>{hint}
    <c:Pager DockPanel.Dock="Bottom" />
    <DataGrid Name="grid" ItemsSource="{{Binding Items}}" SelectedItem="{{Binding Selected}}"{sort}{opn}{rows}>
      <DataGrid.Columns>
{cols}
      </DataGrid.Columns>
    </DataGrid>
  </DockPanel>
</UserControl>
'''

if __name__ == "__main__":
    specs = json.load(open(sys.argv[1]))
    out = sys.argv[2]
    for s in specs:
        with open(os.path.join(out, s["name"] + ".axaml"), "w") as f:
            f.write(gen(s))
        print("generated", s["name"])
