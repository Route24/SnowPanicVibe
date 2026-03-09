/// <summary>雪の3層タイプ。上層=Powder, 中層=Slab, 下層=Base。</summary>
public enum SnowLayerType
{
    Powder,  // 表面雪: 軽い, 小粒で崩れやすい, パラパラ
    Slab,    // 中間雪: 数〜十数個まとまって滑る, 気持ちよさ担当
    Base     // 根雪: 残りやすい, 条件で大きな塊で落ちる
}
