#if UNITY_2019_1_OR_NEWER
using System.Collections.Generic;
using System.Linq;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using static UnityEditor.GenericMenu;

namespace XNodeEditor
{
    /// <summary>
    /// 基于 AdvancedDropdown 的多级 GenericMenu 替代品：接口对齐 Unity GenericMenu，
    /// 但按 "a/b/c" 路径自动建层级，带搜索框，且菜单项回调可带参。
    /// 在 NodeEditorAction/NodeGraphEditor 中经 using 别名替换 GenericMenu 使用。
    /// </summary>
    public class AdvancedGenericMenu : AdvancedDropdown
    {
        public static float? DefaultMinWidth = 200f;
        public static float? DefaultMaxWidth = 300f;

        /// <summary> 菜单项包装：持有无参/带参回调之一，供点选时执行 </summary>
        private class AdvancedGenericMenuItem : AdvancedDropdownItem
        {
            private MenuFunction func;

            private MenuFunction2 func2;
            private object userData;

            /// <summary> 创建仅含名称的目录项（无点击行为） </summary>
            public AdvancedGenericMenuItem( string name ) : base( name )
            {
            }

            /// <summary> 创建带启用态/图标/无参回调的叶子项 </summary>
            public AdvancedGenericMenuItem( string name, bool enabled, Texture2D icon, MenuFunction func ) : base( name )
            {
                Set( enabled, icon, func );
            }

            /// <summary> 创建带启用态/图标/带参回调的叶子项 </summary>
            public AdvancedGenericMenuItem( string name, bool enabled, Texture2D icon, MenuFunction2 func, object userData ) : base( name )
            {
                Set( enabled, icon, func, userData );
            }

            /// <summary> 就地设置无参回调项的启用态、图标与回调 </summary>
            public void Set( bool enabled, Texture2D icon, MenuFunction func )
            {
                this.enabled = enabled;
                this.icon = icon;
                this.func = func;
            }

            /// <summary> 就地设置带参回调项的启用态、图标、回调与附带数据 </summary>
            public void Set( bool enabled, Texture2D icon, MenuFunction2 func, object userData )
            {
                this.enabled = enabled;
                this.icon = icon;
                this.func2 = func;
                this.userData = userData;
            }

            /// <summary> 执行回调：带参回调优先；两者皆为空则什么都不做 </summary>
            public void Run()
            {
                if ( func2 != null )
                    func2( userData );
                else if ( func != null )
                    func();
            }
        }

        /// <summary> 顶层菜单项列表；AddSeparator 在顶层时插入 null 占位 </summary>
        private List<AdvancedGenericMenuItem> items = new List<AdvancedGenericMenuItem>();

        /// <summary>
        /// 按 "a/b/c" 路径逐级查找菜单项，缺失层级立即创建；
        /// currentRoot 为 null 时从顶层开始，否则在该目录的子级中查找；
        /// 路径空白时返回 null。
        /// </summary>
        private AdvancedGenericMenuItem FindOrCreateItem( string name, AdvancedGenericMenuItem currentRoot = null )
        {
            if ( string.IsNullOrWhiteSpace( name ) )
                return null;

            AdvancedGenericMenuItem item = null;

            string[] paths = name.Split( '/' );
            if ( currentRoot == null )
            {
                item = items.FirstOrDefault( x => x != null && x.name == paths[0] );
                if ( item == null )
                    items.Add( item = new AdvancedGenericMenuItem( paths[0] ) );
            }
            else
            {
#if UNITY_6000_5_OR_NEWER
                // Unity 6.5 起 AdvancedDropdownItem.children 更名为 childList
                item = currentRoot.childList.OfType<AdvancedGenericMenuItem>().FirstOrDefault( x => x.name == paths[0] );
#else
                item = currentRoot.children.OfType<AdvancedGenericMenuItem>().FirstOrDefault( x => x.name == paths[0] );
#endif
                if ( item == null )
                    currentRoot.AddChild( item = new AdvancedGenericMenuItem( paths[0] ) );
            }

            if ( paths.Length > 1 )
                return FindOrCreateItem( string.Join( "/", paths, 1, paths.Length - 1 ), item );

            return item;
        }

        /// <summary> 取路径的父级目录项：截掉最后一段后逐级查找/创建 </summary>
        private AdvancedGenericMenuItem FindParent( string name )
        {
            string[] paths = name.Split( '/' );
            return FindOrCreateItem( string.Join( "/", paths, 0, paths.Length - 1 ) );
        }

        /// <summary> 根节点标题；默认空字符串时下拉树根不显示标题 </summary>
        private string Name { get; set; }

        /// <summary> 创建无标题菜单，展开/选中状态使用全新的 AdvancedDropdownState </summary>
        public AdvancedGenericMenu() : base( new AdvancedDropdownState() )
        {
            Name = "";
        }

        /// <summary> 创建指定标题的菜单；state 由调用方持有，可跨开关记忆展开状态 </summary>
        public AdvancedGenericMenu( string name, AdvancedDropdownState state ) : base( state )
        {
            Name = name;
        }

        /// <summary> 向菜单添加一个禁用项（置灰、不可点击）。</summary>
        /// <param name="content">禁用菜单项显示的 GUIContent。</param>
        public void AddDisabledItem( GUIContent content )
        {
            //var parent = FindParent( content.text );
            var item = FindOrCreateItem( content.text );
            item.Set( false, null, null );
        }

        //
        // Summary:
        //     向菜单添加一个禁用项。
        //
        // Parameters:
        //   content:
        //     禁用菜单项显示的 GUIContent。
        //
        //   on:
        //     指定是否显示该项当前处于激活状态（菜单中该项旁显示勾选标记）。
        /// <summary> 对齐 Unity GenericMenu 的重载；当前实现为空，调用不产生任何效果 </summary>
        public void AddDisabledItem( GUIContent content, bool on )
        {
        }

        /// <summary> 按路径文本添加无参回调项；中间层级不存在时自动创建目录 </summary>
        public void AddItem( string name, bool on, MenuFunction func )
        {
            AddItem( new GUIContent( name ), on, func );
        }

        /// <summary> 按 GUIContent.text 路径添加无参回调项；on 参数当前不生效（不渲染勾选） </summary>
        public void AddItem( GUIContent content, bool on, MenuFunction func )
        {
            //var parent = FindParent( content.text );
            var item = FindOrCreateItem( content.text );
            item.Set( true/*on*/, null, func );
        }

        /// <summary> 按路径文本添加带参回调项，userData 原样传给回调 </summary>
        public void AddItem( string name, bool on, MenuFunction2 func, object userData )
        {
            AddItem( new GUIContent( name ), on, func, userData );
        }

        /// <summary> 按 GUIContent.text 路径添加带参回调项；on 参数当前不生效（不渲染勾选） </summary>
        public void AddItem( GUIContent content, bool on, MenuFunction2 func, object userData )
        {
            //var parent = FindParent( content.text );
            var item = FindOrCreateItem( content.text );
            item.Set( true/*on*/, null, func, userData );
        }

        /// <summary> 在菜单中添加分隔项。</summary>
        /// <param name="path">给子菜单加分隔符时传该子菜单路径；给顶层加分隔符时传空/空白。</param>
        public void AddSeparator( string path = null )
        {
            var parent = string.IsNullOrWhiteSpace( path ) ? null : FindParent( path );
            if ( parent == null )
                items.Add( null );
            else
                parent.AddSeparator();
        }

        /// <summary> 在指定屏幕矩形处弹出菜单。</summary>
        /// <param name="position">菜单显示位置；宽度被限制在 DefaultMinWidth/DefaultMaxWidth 之间。</param>
        public void DropDown( Rect position )
        {
            position.width = Mathf.Clamp( position.width, DefaultMinWidth.HasValue ? DefaultMinWidth.Value : 1f, DefaultMaxWidth.HasValue ? DefaultMaxWidth.Value : Screen.width );

            Show( position );
        }

        /// <summary> 构建下拉树根节点：按添加顺序挂载顶层项，null 占位渲染为分隔线 </summary>
        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem( Name );

            foreach ( var m in items )
            {
                if ( m == null )
                    root.AddSeparator();
                else
                    root.AddChild( m );
            }

            return root;
        }

        /// <summary> 用户点选叶子项时回调；命中本包装的项则执行其回调 </summary>
        protected override void ItemSelected( AdvancedDropdownItem item )
        {
            if ( item is AdvancedGenericMenuItem gmItem )
                gmItem.Run();
        }
    }
}
#endif
